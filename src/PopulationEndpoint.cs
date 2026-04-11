using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Timberborn.Beavers;
using Timberborn.Characters;
using Timberborn.DwellingSystem;
using Timberborn.EntityNaming;
using Timberborn.HttpApiSystem;
using Timberborn.NeedSystem;
using Timberborn.Population;
using Timberborn.Wellbeing;
using UnityEngine;

namespace HTTPAutomation
{
    public class PopulationEndpoint : IHttpApiEndpoint
    {
        private const string PopRoute   = "/api/population";
        private const string BeaverBase = "/api/beavers";

        // Worker reflection cache
        private bool         _workerResolved;
        private PropertyInfo _workerEmployedProp;
        private PropertyInfo _workerWorkplaceProp;
        private PropertyInfo _workplaceNameProp;
        private MethodInfo   _workplaceUnassignMethod;
        private readonly List<Component> _scratch = new List<Component>();

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path   = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

            if (!path.Equals(PopRoute, StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith(BeaverBase, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);
            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }

            try   { await Dispatch(ctx, path, method); }
            catch (Exception ex) { await HttpResponseHelper.WriteError(ctx, 500, ex.Message); }
            return true;
        }

        private async Task Dispatch(HttpListenerContext ctx, string path, string method)
        {
            if (!GameServices.Ready)
            { await HttpResponseHelper.WriteError(ctx, 503, "Game not loaded yet"); return; }

            var pop = GameServices.BeaverPopulation;
            if (pop == null && GameServices.Population == null)
            { await HttpResponseHelper.WriteJson(ctx, 200, "{\"totalPopulation\":0,\"totalBeavers\":0,\"adults\":0,\"children\":0,\"bots\":0,\"unemployed\":0,\"homeless\":0,\"injured\":0,\"contaminated\":0}"); return; }

            if (path.Equals(PopRoute, StringComparison.OrdinalIgnoreCase))
            {
                if (method != "GET") { await HttpResponseHelper.WriteError(ctx, 405, "Use GET"); return; }
                await HttpResponseHelper.WriteJson(ctx, 200, BuildPopJson(pop));
                return;
            }

            var tail  = path.Substring(BeaverBase.Length).Trim('/');
            var parts = tail.Length > 0
                ? tail.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                : Array.Empty<string>();

            if (parts.Length == 0 && method == "GET")
            {
                List<Beaver> snapshot;
                lock (GameServices.BeaversLock)
                    snapshot = new List<Beaver>(GameServices.Beavers);
                await HttpResponseHelper.WriteJson(ctx, 200, BuildListJson(snapshot));
                return;
            }

            if (parts.Length == 1 && method == "GET")
            {
                BeaverSnap found = null;
                lock (GameServices.BeaversLock)
                    found = FindById(GameServices.Beavers, parts[0]);
                if (found == null) { await HttpResponseHelper.WriteError(ctx, 404, "Not found"); return; }
                await HttpResponseHelper.WriteJson(ctx, 200, ToJson(found));
                return;
            }

            if (parts.Length == 2
                && parts[1].Equals("dismiss", StringComparison.OrdinalIgnoreCase)
                && method == "POST")
            {
                bool ok = false;
                lock (GameServices.BeaversLock)
                    ok = TryDismiss(GameServices.Beavers, parts[0]);
                await HttpResponseHelper.WriteJson(ctx, 200,
                    ok ? "{\"success\":true}" : "{\"success\":false,\"error\":\"Not found or no job\"}");
                return;
            }

            await HttpResponseHelper.WriteError(ctx, 404, "Unknown: " + path);
        }

        // ── Population aggregate ──────────────────────────────────────────────

        private static string GetId(Beaver b)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b).ToString();

        private static string BuildPopJson(BeaverPopulation pop)
        {
            // Build a snapshot of beavers to compute averages
            List<Beaver> snapshot;
            lock (GameServices.BeaversLock)
                snapshot = new List<Beaver>(GameServices.Beavers);

            float sumHunger = 0f, sumThirst = 0f, sumSleep = 0f;
            int   sumWellbeing = 0, wellbeingCount = 0;
            int   count = 0, injuredCount = 0;

            foreach (var b in snapshot)
            {
                try
                {
                    var nm = b.GetComponent<NeedManager>();
                    if (nm != null)
                    {
                        if (nm.HasNeed("Hunger")) sumHunger += nm.GetNeedPoints("Hunger");
                        if (nm.HasNeed("Thirst")) sumThirst += nm.GetNeedPoints("Thirst");
                        if (nm.HasNeed("Sleep"))  sumSleep  += nm.GetNeedPoints("Sleep");
                        if (nm.HasNeed("Injury") && nm.GetNeedPoints("Injury") < -0.05f) injuredCount++;
                        count++;
                    }
                    var wt = b.GetComponent<WellbeingTracker>();
                    if (wt != null)
                    {
                        sumWellbeing += wt.Wellbeing;
                        wellbeingCount++;
                    }
                }
                catch { /* skip malformed beaver */ }
            }

            float avgHunger    = count > 0 ? sumHunger / count : 0f;
            float avgThirst    = count > 0 ? sumThirst / count : 0f;
            float avgSleep     = count > 0 ? sumSleep  / count : 0f;
            // Prefer the game's own pre-computed colony average when available
            int   avgWellbeing = GameServices.WellbeingSvc != null
                ? GameServices.WellbeingSvc.AverageGlobalWellbeing
                : (wellbeingCount > 0 ? sumWellbeing / wellbeingCount : 0);
            int   maxWellbeing = GameServices.WellbeingLimit?.MaxBeaverWellbeing ?? 0;

            var svc = GameServices.Population;
            if (svc != null)
            {
                var d = svc.GlobalPopulationData;
                return "{\"totalPopulation\":"   + d.TotalPopulation
                    + ",\"totalBeavers\":"       + d.NumberOfBeavers
                    + ",\"adults\":"             + d.NumberOfAdults
                    + ",\"children\":"           + d.NumberOfChildren
                    + ",\"bots\":"               + d.NumberOfBots
                    + ",\"unemployed\":"         + d.BeaverWorkforceData.Unemployable
                    + ",\"homeless\":"           + d.BedData.Homeless
                    + ",\"injured\":"            + injuredCount
                    + ",\"contaminated\":"       + d.ContaminationData.ContaminatedTotal
                    + ",\"averageHunger\":"      + HttpResponseHelper.Fmt(avgHunger)
                    + ",\"averageThirst\":"      + HttpResponseHelper.Fmt(avgThirst)
                    + ",\"averageSleep\":"       + HttpResponseHelper.Fmt(avgSleep)
                    + ",\"averageWellbeing\":"   + avgWellbeing
                    + ",\"maxWellbeing\":"       + maxWellbeing + "}";
            }
            // Fallback to BeaverPopulation
            return "{\"totalPopulation\":"   + pop.NumberOfBeavers
                + ",\"totalBeavers\":"       + pop.NumberOfBeavers
                + ",\"adults\":"             + pop.NumberOfAdults
                + ",\"children\":"           + pop.NumberOfChildren
                + ",\"bots\":0"
                + ",\"unemployed\":0,\"homeless\":0,\"injured\":" + injuredCount + ",\"contaminated\":0"
                + ",\"averageHunger\":"      + HttpResponseHelper.Fmt(avgHunger)
                + ",\"averageThirst\":"      + HttpResponseHelper.Fmt(avgThirst)
                + ",\"averageSleep\":"       + HttpResponseHelper.Fmt(avgSleep)
                + ",\"averageWellbeing\":"   + avgWellbeing
                + ",\"maxWellbeing\":"       + maxWellbeing + "}";
        }

        // ── Beaver list / single ──────────────────────────────────────────────

        private string BuildListJson(List<Beaver> beavers)
        {
            var sb = new StringBuilder("["); bool first = true;
            foreach (var b in beavers)
            {
                BeaverSnap snap;
                try { snap = Snapshot(b); }
                catch (Exception ex)
                {
                    ModLog.Error("Snapshot threw for beaver id=" + GetId(b), ex);
                    continue;
                }
                if (!first) sb.Append(',');
                first = false;
                sb.Append(ToJson(snap));
            }
            sb.Append(']'); return sb.ToString();
        }

        private BeaverSnap FindById(List<Beaver> beavers, string id)
        {
            foreach (var b in beavers) if (GetId(b) == id) return Snapshot(b);
            return null;
        }

        private bool TryDismiss(List<Beaver> beavers, string id)
        {
            foreach (var b in beavers) if (GetId(b) == id) return DismissWorker(b);
            return false;
        }

        private BeaverSnap Snapshot(Beaver b)
        {
            var s = new BeaverSnap { Id = GetId(b) };
            s.Name    = b.GetComponent<NamedEntity>()?.EntityName ?? "Beaver";
            s.IsAdult = b.GetComponent<Child>() == null;
            var ch = b.GetComponent<Character>();
            if (ch != null) s.AgeInDays = Mathf.Max(0,
                (GameServices.DayNightCycle?.DayNumber ?? 0) - ch.DayOfBirth);

            var nm = b.GetComponent<NeedManager>();
            if (nm != null)
            {
                s.Hunger        = nm.HasNeed("Hunger")                ? nm.GetNeedPoints("Hunger")                : 0f;
                s.Thirst        = nm.HasNeed("Thirst")                ? nm.GetNeedPoints("Thirst")                : 0f;
                s.Sleep         = nm.HasNeed("Sleep")                 ? nm.GetNeedPoints("Sleep")                 : 0f;
                s.Injury        = nm.HasNeed("Injury")                ? nm.GetNeedPoints("Injury")                : 0f;
                s.Contamination = nm.HasNeed("BadwaterContamination") ? nm.GetNeedPoints("BadwaterContamination") : 0f;

                // Wellbeing: per-need breakdown for all needs that affect wellbeing
                s.Needs = BuildNeedsList(nm);
            }
            s.IsInjured = s.Injury < -0.05f; s.IsContaminated = s.Contamination < -0.05f;
            s.IsHungry  = s.Hunger < 0f;     s.IsThirsty      = s.Thirst        < 0f;

            // Wellbeing totals from WellbeingTracker component
            var wt = b.GetComponent<WellbeingTracker>();
            if (wt != null)
            {
                s.Wellbeing    = wt.Wellbeing;
                s.MaxWellbeing = GameServices.WellbeingLimit != null
                    ? GameServices.WellbeingLimit.GetMaxWellbeing(wt)
                    : (GameServices.WellbeingLimit?.MaxBeaverWellbeing ?? 0);
            }

            s.HasJob    = WorkerEmployed(b);
            s.Workplace = WorkplaceName(b);
            var d = b.GetComponent<Dweller>();
            s.HasHome = d != null && d.HasHome;
            return s;
        }

        /// <summary>
        /// Iterates all NeedSpecs on a NeedManager and builds a list of needs that
        /// affect wellbeing, including the current and maximum wellbeing contribution.
        /// This lets the frontend compute dynamic max wellbeing per-beaver (e.g. beavers
        /// that have coffee access have a higher max than those who don't).
        /// </summary>
        private static List<NeedEntry> BuildNeedsList(NeedManager nm)
        {
            var result = new List<NeedEntry>();
            try
            {
                foreach (var spec in nm.NeedSpecs)
                {
                    if (!spec.AffectsWellbeing) continue;
                    // Only include needs active for this beaver
                    if (!nm.HasNeed(spec.Id) || !nm.NeedIsActive(spec.Id)) continue;

                    var entry = new NeedEntry();
                    entry.Id             = spec.Id;
                    entry.Name           = spec.DisplayNameLocKey ?? spec.Id;
                    entry.WellbeingMax   = spec.GetFavorableWellbeing();
                    entry.WellbeingBad   = spec.GetUnfavorableWellbeing(); // ≤0
                    entry.WellbeingNow   = nm.GetNeedWellbeing(spec.Id);
                    entry.IsFavorable    = nm.NeedIsFavorable(spec.Id);
                    entry.Points         = nm.GetNeedPoints(spec.Id);
                    result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn("BuildNeedsList threw: " + ex.Message);
            }
            return result;
        }

        // ── Worker/Workplace reflection ───────────────────────────────────────

        private Component GetWorker(Beaver b)
        {
            _scratch.Clear(); b.GetComponents<Component>(_scratch);
            foreach (var c in _scratch) if (c != null && c.GetType().Name == "Worker") return c;
            return null;
        }

        private void EnsureWorkerReflection(Component w)
        {
            if (_workerResolved) return; _workerResolved = true;
            var t = w.GetType();
            _workerEmployedProp  = t.GetProperty("Employed");
            _workerWorkplaceProp = t.GetProperty("Workplace");
            if (_workerWorkplaceProp != null)
            {
                var wp = _workerWorkplaceProp.GetValue(w);
                if (wp != null)
                {
                    var wt = wp.GetType();
                    _workplaceNameProp       = wt.GetProperty("WorkplaceTemplateName") ?? wt.GetProperty("TemplateName");
                    _workplaceUnassignMethod = wt.GetMethod("UnassignWorker", BindingFlags.Public | BindingFlags.Instance);
                }
            }
        }

        private bool WorkerEmployed(Beaver b)
        {
            try
            {
                var w = GetWorker(b); if (w == null) return false;
                EnsureWorkerReflection(w);
                return _workerEmployedProp != null && (bool)_workerEmployedProp.GetValue(w);
            }
            catch { return false; }
        }

        private string WorkplaceName(Beaver b)
        {
            try
            {
                var w = GetWorker(b); if (w == null) return "";
                EnsureWorkerReflection(w); if (_workerWorkplaceProp == null) return "";
                var wp = _workerWorkplaceProp.GetValue(w); if (wp == null) return "";
                if (_workplaceNameProp == null)
                    _workplaceNameProp = wp.GetType().GetProperty("WorkplaceTemplateName")
                                      ?? wp.GetType().GetProperty("TemplateName");
                return _workplaceNameProp != null ? (_workplaceNameProp.GetValue(wp) as string ?? "") : "";
            }
            catch { return ""; }
        }

        private bool DismissWorker(Beaver b)
        {
            try
            {
                var w = GetWorker(b); if (w == null) return false;
                EnsureWorkerReflection(w);
                if (_workerEmployedProp == null || !(bool)_workerEmployedProp.GetValue(w)) return false;
                if (_workerWorkplaceProp != null)
                {
                    var wp = _workerWorkplaceProp.GetValue(w); if (wp == null) return false;
                    if (_workplaceUnassignMethod == null)
                        _workplaceUnassignMethod = wp.GetType().GetMethod("UnassignWorker", BindingFlags.Public | BindingFlags.Instance);
                    if (_workplaceUnassignMethod != null)
                    {
                        _workplaceUnassignMethod.Invoke(wp, new object[] { w });
                        return true;
                    }
                }
                return false;
            }
            catch (Exception ex) { ModLog.Error("DismissWorker threw", ex); return false; }
        }

        // ── JSON serialization ────────────────────────────────────────────────

        private static string ToJson(BeaverSnap s)
        {
            var sb = new StringBuilder();
            sb.Append("{\"id\":\"")           .Append(HttpResponseHelper.Escape(s.Id));
            sb.Append("\",\"name\":\"")       .Append(HttpResponseHelper.Escape(s.Name));
            sb.Append("\",\"ageInDays\":")    .Append(s.AgeInDays);
            sb.Append(",\"isAdult\":")        .Append(HttpResponseHelper.Bool(s.IsAdult));
            sb.Append(",\"hunger\":")         .Append(HttpResponseHelper.Fmt(s.Hunger));
            sb.Append(",\"thirst\":")         .Append(HttpResponseHelper.Fmt(s.Thirst));
            sb.Append(",\"sleep\":")          .Append(HttpResponseHelper.Fmt(s.Sleep));
            sb.Append(",\"injury\":")         .Append(HttpResponseHelper.Fmt(s.Injury));
            sb.Append(",\"contamination\":") .Append(HttpResponseHelper.Fmt(s.Contamination));
            sb.Append(",\"isInjured\":")      .Append(HttpResponseHelper.Bool(s.IsInjured));
            sb.Append(",\"isContaminated\":").Append(HttpResponseHelper.Bool(s.IsContaminated));
            sb.Append(",\"isHungry\":")       .Append(HttpResponseHelper.Bool(s.IsHungry));
            sb.Append(",\"isThirsty\":")      .Append(HttpResponseHelper.Bool(s.IsThirsty));
            sb.Append(",\"hasJob\":")         .Append(HttpResponseHelper.Bool(s.HasJob));
            sb.Append(",\"hasHome\":")        .Append(HttpResponseHelper.Bool(s.HasHome));
            sb.Append(",\"workplace\":\"")    .Append(HttpResponseHelper.Escape(s.Workplace));
            sb.Append("\",\"wellbeing\":")    .Append(s.Wellbeing);
            sb.Append(",\"maxWellbeing\":")   .Append(s.MaxWellbeing);

            // Per-need wellbeing breakdown
            sb.Append(",\"needs\":[");
            if (s.Needs != null)
            {
                bool first = true;
                foreach (var n in s.Needs)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"")             .Append(HttpResponseHelper.Escape(n.Id));
                    sb.Append("\",\"name\":\"")         .Append(HttpResponseHelper.Escape(n.Name));
                    sb.Append("\",\"wellbeingNow\":")   .Append(n.WellbeingNow);
                    sb.Append(",\"wellbeingMax\":")      .Append(n.WellbeingMax);
                    sb.Append(",\"wellbeingBad\":")      .Append(n.WellbeingBad);
                    sb.Append(",\"isFavorable\":")       .Append(HttpResponseHelper.Bool(n.IsFavorable));
                    sb.Append(",\"points\":")            .Append(HttpResponseHelper.Fmt(n.Points));
                    sb.Append("}");
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ── Data structures ───────────────────────────────────────────────────

        private class BeaverSnap
        {
            public string Id = "", Name = "", Workplace = "";
            public int    AgeInDays;
            public bool   IsAdult, IsInjured, IsContaminated, IsHungry, IsThirsty, HasJob, HasHome;
            public float  Hunger, Thirst, Sleep, Injury, Contamination;
            public int    Wellbeing, MaxWellbeing;
            public List<NeedEntry> Needs;
        }

        private class NeedEntry
        {
            public string Id, Name;
            public int    WellbeingNow, WellbeingMax, WellbeingBad;
            public bool   IsFavorable;
            public float  Points;
        }
    }
}
