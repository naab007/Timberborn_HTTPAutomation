using System;
using System.Net;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    public class GameStateEndpoint : IHttpApiEndpoint
    {
        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path   = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

            if (path.Equals("/api/debug", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseHelper.AddCorsHeaders(ctx.Response);
                var sb = new System.Text.StringBuilder("[");
                bool first = true;
                if (GameServices.Repo != null)
                    foreach (var s in GameServices.Repo.GetSingletons<object>())
                    { if (!first) sb.Append(','); first = false; sb.Append("\"" + s.GetType().FullName + "\""); }
                sb.Append(']');
                await HttpResponseHelper.WriteJson(ctx, 200, sb.ToString());
                return true;
            }

            if (path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseHelper.AddCorsHeaders(ctx.Response);
                var saveName = HttpResponseHelper.Escape(GameServices.SettlementName);
                int beaverCount;
                lock (GameServices.BeaversLock)
                    beaverCount = GameServices.Beavers.Count;
                await HttpResponseHelper.WriteJson(ctx, 200,
                    "{\"ok\":true,\"mod\":\"HTTPAutomation\",\"version\":\"4.2\",\"ready\":"
                    + HttpResponseHelper.Bool(GameServices.Ready)
                    + ",\"saveName\":\"" + saveName + "\""
                    + ",\"beaverCount\":" + beaverCount
                    + ",\"eventBus\":"   + HttpResponseHelper.Bool(GameServices.EventBus != null)
                    + "}");
                return true;
            }

            if (!path.Equals("/api/gamestate", StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);
            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }
            if (method != "GET")     { await HttpResponseHelper.WriteError(ctx, 405, "Use GET"); return true; }

            if (!GameServices.Ready)
            {
                await HttpResponseHelper.WriteError(ctx, 503, "Game not loaded yet");
                return true;
            }

            try
            {
                var cycle     = GameServices.DayNightCycle;
                var gameCycle = GameServices.GameCycle;
                var hazard    = GameServices.Hazardous?.CurrentCycleHazardousWeather;
                var isDrought = hazard != null && hazard.Id == "Drought";
                var isBadtide = hazard != null && hazard.Id == "Badtide";
                var p         = cycle.DayProgress;
                var isDay     = cycle.IsDaytime;

                string stage = p < 0.15f ? "Sunrise"
                             : p < 0.75f ? (isDay ? "Daytime" : "Sunset")
                             : "Nighttime";

                // ── Moddable Weather support ──────────────────────────────────
                // WeatherCycleService is from the optional ModdableWeathers mod.
                // When present, exposes rich per-stage weather data — the current
                // weather ID, how many days remain, and the next stage's weather.
                string weatherId          = isDrought ? "Drought" : isBadtide ? "Badtide" : "Temperate";
                bool   weatherIsHazardous = GameServices.Weather?.IsHazardousWeather ?? false;
                bool   weatherIsBenign    = !weatherIsHazardous;
                int    weatherDaysInStage = 0;
                int    weatherDaysSinceStart = 0;
                int    weatherDaysRemaining  = 0;
                string nextWeatherId      = "";
                bool   moddableWeather    = false;

                try
                {
                    var wcs = GameServices.WeatherCycle;
                    if (wcs != null)
                    {
                        var wcsType = wcs.GetType();

                        // CurrentStage : DetailedWeatherStageReference
                        //   .Stage     : DetailedWeatherCycleStage / WeatherCycleStage
                        //     .WeatherId (string), .IsBenign (bool)
                        //   .Cycle     : DetailedWeatherCycle
                        //     .Stages  : ImmutableArray<DetailedWeatherCycleStage>
                        var currentStageProp = wcsType.GetProperty("CurrentStage");
                        var currentStageTotalDaysProp = wcsType.GetProperty("CurrentStageTotalDays");
                        var daysSinceProp = wcsType.GetProperty("DaysSinceCurrentStage");

                        if (currentStageProp != null && currentStageTotalDaysProp != null && daysSinceProp != null)
                        {
                            var stageRef = currentStageProp.GetValue(wcs);
                            weatherDaysInStage    = (int)currentStageTotalDaysProp.GetValue(wcs);
                            weatherDaysSinceStart = (int)daysSinceProp.GetValue(wcs);
                            weatherDaysRemaining  = Math.Max(0, weatherDaysInStage - weatherDaysSinceStart);
                            moddableWeather = true;

                            if (stageRef != null)
                            {
                                var stageRefType = stageRef.GetType();
                                // Stage property on DetailedWeatherStageReference
                                var stageProp = stageRefType.GetProperty("Stage");
                                if (stageProp != null)
                                {
                                    var stageObj = stageProp.GetValue(stageRef);
                                    if (stageObj != null)
                                    {
                                        var stageType = stageObj.GetType();
                                        var widProp = stageType.GetProperty("WeatherId");
                                        var benProp = stageType.GetProperty("IsBenign");
                                        if (widProp != null)
                        {
                            var rawId = widProp.GetValue(stageObj) as string ?? weatherId;
                            // Normalize: strip trailing "Weather" suffix so spec IDs like
                            // "DroughtWeather"/"BadtideWeather"/"TemperateWeather" become
                            // "Drought"/"Badtide"/"Temperate" — matching what vanilla returns
                            // and what the frontend checks (gs.weatherId.toLowerCase() === 'drought').
                            weatherId = rawId.EndsWith("Weather")
                                ? rawId.Substring(0, rawId.Length - "Weather".Length)
                                : rawId;
                        }
                                        if (benProp != null) weatherIsBenign = (bool)benProp.GetValue(stageObj);
                                        weatherIsHazardous = !weatherIsBenign;
                                    }
                                }

                                // Derive next stage from the cycle's Stages array
                                var cycleProp = stageRefType.GetProperty("Cycle");
                                var stageIndexProp = stageRefType.GetProperty("StageIndex");
                                if (cycleProp != null && stageIndexProp != null)
                                {
                                    var cycleObj = cycleProp.GetValue(stageRef);
                                    int stageIdx = (int)stageIndexProp.GetValue(stageRef);
                                    if (cycleObj != null)
                                    {
                                        var stagesProp = cycleObj.GetType().GetProperty("Stages");
                                        if (stagesProp != null)
                                        {
                                            var stages = stagesProp.GetValue(cycleObj);
                                            // ImmutableArray — access via IList<T> or index reflection
                                            var countProp = stages?.GetType().GetProperty("Length")
                                                         ?? stages?.GetType().GetProperty("Count");
                                            int count = countProp != null ? (int)countProp.GetValue(stages) : 0;
                                            int nextIdx = stageIdx + 1;
                                            if (nextIdx < count)
                                            {
                                                // Access ImmutableArray[i] via get_Item or indexer
                                                var getItem = stages.GetType().GetMethod("get_Item") ??
                                                              stages.GetType().GetMethod("ItemRef");
                                                if (getItem == null)
                                                {
                                                    // Fallback: convert to array
                                                    var toArray = stages.GetType().GetMethod("ToArray",
                                                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                                    if (toArray != null)
                                                    {
                                                        var arr = toArray.Invoke(stages, null) as Array;
                                                        if (arr != null && nextIdx < arr.Length)
                                                        {
                                                            var nextStage = arr.GetValue(nextIdx);
                                                            var nwid = nextStage?.GetType().GetProperty("WeatherId")?.GetValue(nextStage) as string;
                                                            if (nwid != null) nextWeatherId = nwid;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    var nextStage = getItem.Invoke(stages, new object[] { nextIdx });
                                                    var nwid = nextStage?.GetType().GetProperty("WeatherId")?.GetValue(nextStage) as string;
                                                    if (nwid != null) nextWeatherId = nwid;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLog.Warn("ModdableWeather data read threw: " + ex.Message);
                }
                // ──────────────────────────────────────────────────────────────

                // Keep backward-compat isDrought/isBadtide — derive from weatherId when ModdableWeather is active.
                // When ModdableWeathers is active, weatherId is the spec ID (e.g. "DroughtWeather", "BadtideWeather"),
                // NOT the short form ("Drought", "Badtide") used by vanilla HazardousWeatherService.
                if (moddableWeather)
                {
                    isDrought = weatherId == "DroughtWeather" || weatherId == "Drought";
                    isBadtide = weatherId == "BadtideWeather" || weatherId == "Badtide";
                }

                var json = "{\"cycleNumber\":"      + gameCycle.Cycle
                    + ",\"dayNumber\":"             + cycle.DayNumber
                    + ",\"dayProgress\":"           + HttpResponseHelper.Fmt(p)
                    + ",\"timeOfDayHours\":"        + HttpResponseHelper.Fmt(cycle.HoursPassedToday)
                    + ",\"dayStage\":\""            + stage + "\""
                    + ",\"isDay\":"                 + HttpResponseHelper.Bool(isDay)
                    + ",\"isNight\":"               + HttpResponseHelper.Bool(cycle.IsNighttime)
                    + ",\"weather\":\""             + (isDrought ? "Drought" : isBadtide ? "Badtide" : "Temperate") + "\""
                    + ",\"isDrought\":"             + HttpResponseHelper.Bool(isDrought)
                    + ",\"isBadtide\":"             + HttpResponseHelper.Bool(isBadtide)
                    + ",\"isHazardous\":"           + HttpResponseHelper.Bool(weatherIsHazardous)
                    + ",\"weatherId\":\""           + HttpResponseHelper.Escape(weatherId) + "\""
                    + ",\"weatherIsHazardous\":"    + HttpResponseHelper.Bool(weatherIsHazardous)
                    + ",\"weatherDaysInStage\":"    + weatherDaysInStage
                    + ",\"weatherDaysSinceStart\":" + weatherDaysSinceStart
                    + ",\"weatherDaysRemaining\":"  + weatherDaysRemaining
                    + ",\"nextWeatherId\":\""       + HttpResponseHelper.Escape(nextWeatherId) + "\""
                    + ",\"moddableWeather\":"       + HttpResponseHelper.Bool(moddableWeather)
                    + ",\"saveName\":\""            + HttpResponseHelper.Escape(GameServices.SettlementName) + "\""
                    + "}";

                await HttpResponseHelper.WriteJson(ctx, 200, json);
            }
            catch (Exception ex)
            {
                ModLog.Error("/api/gamestate threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }
            return true;
        }
    }
}
