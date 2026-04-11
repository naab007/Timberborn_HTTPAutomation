using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Timberborn.Beavers;
using Timberborn.Characters;
using Timberborn.GameCycleSystem;
using Timberborn.HazardousWeatherSystem;
using Timberborn.Population;
using Timberborn.SettlementNameSystem;
using Timberborn.SingletonSystem;
using Timberborn.TimeSystem;
using Timberborn.Wellbeing;
using Timberborn.WeatherSystem;

namespace HTTPAutomation
{
    public static class GameServices
    {
        public static IDayNightCycle             DayNightCycle;
        public static GameCycleService           GameCycle;
        public static WeatherService             Weather;
        public static HazardousWeatherService    Hazardous;
        public static BeaverPopulation           BeaverPopulation;   // fallback only
        public static PopulationService          Population;         // preferred
        public static SettlementReferenceService SettlementRef;      // settlement name
        public static EventBus                   EventBus;

        // HttpApiIntermediary is internal to Timberborn.HttpApiSystem — stored as object,
        // accessed via reflection in LeverEndpoint.
        public static object Intermediary;

        // WeatherCycleService is from ModdableWeathers mod (optional).
        // When present, provides rich per-stage weather info for /api/gamestate.
        // Stored as object — internal to ModdableWeathers.dll.
        public static object WeatherCycle;

        // AutomatorRegistry — provides live list of all transmitter buildings (sensors).
        // Stored as object — accessed via reflection in SensorEndpoint.
        public static object AutomatorReg;

        // Wellbeing singletons — used by PopulationEndpoint for per-beaver and colony stats.
        public static WellbeingService      WellbeingSvc;   // AverageGlobalWellbeing (int)
        public static WellbeingLimitService WellbeingLimit; // GetMaxWellbeing(tracker), MaxBeaverWellbeing

        public static ISingletonRepository Repo;

        // Live beaver list maintained by [OnEvent] in GameServicesInitializer.
        // Locked because HTTP handlers run on thread-pool threads.
        public static readonly List<Beaver> Beavers     = new List<Beaver>();
        public static readonly object       BeaversLock = new object();

        // Atomic claim flags — 0 = available, 1 = claimed.
        // Exactly one of the two Bindito containers per session wins each CAS.
        // Winning Unload() resets both so the next session can claim them.
        internal static int LoadClaimed   = 0;
        internal static int UnloadClaimed = 0;

        public static bool Ready;

        public static GameServicesInitializer RegisteredInitializer;

        public static string SettlementName =>
            SettlementRef?.SettlementReference?.SettlementName ?? "";

        // ── Lever helper (called by LeverEndpoint) ────────────────────────────

        /// <summary>
        /// Calls HttpApiIntermediary.GetLevers() via reflection and returns each lever
        /// as a plain struct so LeverEndpoint never needs to reference the internal type.
        /// </summary>
        public struct LeverInfo
        {
            public string Name;
            public bool   State;
            public bool   IsSpringReturn;
        }

        public static LeverInfo[] GetLevers()
        {
            if (Intermediary == null) return new LeverInfo[0];
            try
            {
                var method  = Intermediary.GetType().GetMethod("GetLevers",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return new LeverInfo[0];

                var result  = method.Invoke(Intermediary, null); // ImmutableArray<HttpLeverSnapshot>
                var enumerable = result as IEnumerable;
                if (enumerable == null) return new LeverInfo[0];

                var list = new List<LeverInfo>();
                foreach (var snap in enumerable)
                {
                    var t    = snap.GetType();
                    var name = t.GetProperty("Name")?.GetValue(snap) as string ?? "";
                    var state = (bool)(t.GetProperty("State")?.GetValue(snap) ?? false);
                    var isSR  = (bool)(t.GetProperty("IsSpringReturn")?.GetValue(snap) ?? false);
                    list.Add(new LeverInfo { Name = name, State = state, IsSpringReturn = isSR });
                }
                return list.ToArray();
            }
            catch (Exception ex)
            {
                ModLog.Error("GameServices.GetLevers() reflection threw", ex);
                return new LeverInfo[0];
            }
        }
    }

    public class GameServicesInitializer : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly ISingletonRepository _repo;

        private static readonly string ServicesLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Timberborn", "Mods", "HTTPAutomation", "services.log");

        public GameServicesInitializer(ISingletonRepository repo) { _repo = repo; }

        // ── ILoadableSingleton ────────────────────────────────────────────────

        public void Load()
        {
            // The CAS guard must come FIRST. Both containers must NOT reset UnloadClaimed
            // before the CAS — if they do, the previous session's winning Unload() left
            // UnloadClaimed=1, but both Load() callers reset it to 0 before checking
            // LoadClaimed, allowing Unload #2 (from the old session) to see 0 and also win.
            // Only the winner of LoadClaimed resets UnloadClaimed.
            if (Interlocked.CompareExchange(ref GameServices.LoadClaimed, 1, 0) != 0)
            {
                ModLog.Info("GameServicesInitializer.Load() — skipped (claimed by concurrent container)");
                return;
            }

            // Winner only: reset UnloadClaimed so THIS session's Unload() can run.
            // The losing Load() never reaches here, so it never touches UnloadClaimed.
            Interlocked.Exchange(ref GameServices.UnloadClaimed, 0);

            ModLog.Info("GameServicesInitializer.Load() — starting service resolution");

            GameServices.Repo = _repo;
            var found = new List<string>();

            foreach (var s in _repo.GetSingletons<object>())
                Assign(s, found);

            GameServices.Ready = GameServices.DayNightCycle != null;

            ModLog.Info("Service resolution results:");
            ModLog.Info("  DayNightCycle:    " + (GameServices.DayNightCycle    != null ? "OK" : "MISSING"));
            ModLog.Info("  GameCycle:        " + (GameServices.GameCycle        != null ? "OK" : "MISSING"));
            ModLog.Info("  Weather:          " + (GameServices.Weather          != null ? "OK" : "MISSING"));
            ModLog.Info("  Hazardous:        " + (GameServices.Hazardous        != null ? "OK" : "MISSING"));
            ModLog.Info("  Population:       " + (GameServices.Population       != null ? "OK" : "MISSING"));
            ModLog.Info("  BeaverPopulation: " + (GameServices.BeaverPopulation != null ? "OK" : "MISSING"));
            ModLog.Info("  SettlementRef:    " + (GameServices.SettlementRef    != null ? "OK" : "MISSING"));
            ModLog.Info("  EventBus:         " + (GameServices.EventBus         != null ? "OK" : "MISSING"));
            ModLog.Info("  Intermediary:     " + (GameServices.Intermediary     != null ? "OK" : "MISSING"));
            ModLog.Info("  WeatherCycle:     " + (GameServices.WeatherCycle  != null ? "OK (ModdableWeathers)" : "not present"));
            ModLog.Info("  AutomatorReg:     " + (GameServices.AutomatorReg  != null ? "OK" : "MISSING"));
            ModLog.Info("  WellbeingSvc:     " + (GameServices.WellbeingSvc    != null ? "OK" : "MISSING"));
            ModLog.Info("  WellbeingLimit:   " + (GameServices.WellbeingLimit  != null ? "OK" : "MISSING"));
            ModLog.Info("  Ready:            " + GameServices.Ready);

            var sName = GameServices.SettlementName;
            if (!string.IsNullOrEmpty(sName))
                ModLog.Info("  SettlementName:   " + sName);
            else
                ModLog.Warn("  SettlementName:   empty — saveName will be empty in /api/gamestate");

            try
            {
                File.WriteAllText(ServicesLogPath,
                    "=== GameServices.Load() ===\n" +
                    "DayNightCycle:   " + (GameServices.DayNightCycle   != null) + "\n" +
                    "GameCycle:       " + (GameServices.GameCycle       != null) + "\n" +
                    "Weather:         " + (GameServices.Weather         != null) + "\n" +
                    "Hazardous:       " + (GameServices.Hazardous       != null) + "\n" +
                    "Population:      " + (GameServices.Population      != null) + "\n" +
                    "BeaverPopulation:" + (GameServices.BeaverPopulation != null) + "\n" +
                    "SettlementRef:   " + (GameServices.SettlementRef   != null) + "\n" +
                    "Intermediary:    " + (GameServices.Intermediary    != null) + "\n" +
                    "SettlementName:  " + GameServices.SettlementName + "\n" +
                    "EventBus:        " + (GameServices.EventBus        != null) + "\n" +
                    "Ready:           " + GameServices.Ready + "\n\n" +
                    "All found: " + string.Join(", ", found) + "\n");
            }
            catch (Exception ex) { ModLog.Error("Failed to write services.log", ex); }

            if (GameServices.EventBus != null)
            {
                try
                {
                    GameServices.EventBus.Register(this);
                    GameServices.RegisteredInitializer = this;
                    ModLog.Info("EventBus.Register(this) succeeded — beaver tracking active");
                }
                catch (Exception ex)
                {
                    ModLog.Error("EventBus.Register(this) failed — /api/beavers will return []", ex);
                }
            }
            else
            {
                ModLog.Warn("EventBus is null — cannot register, /api/beavers will return []");
            }

            ModLog.Info("GameServicesInitializer.Load() — complete. SettlementName=\"" + GameServices.SettlementName + "\"");
        }

        // ── IUnloadableSingleton ──────────────────────────────────────────────

        public void Unload()
        {
            if (Interlocked.CompareExchange(ref GameServices.UnloadClaimed, 1, 0) != 0)
            {
                ModLog.Info("GameServicesInitializer.Unload() — skipped (claimed by concurrent container)");
                return;
            }

            ModLog.Info("GameServicesInitializer.Unload() — game scene tearing down, resetting all state");
            ResetState();
            Interlocked.Exchange(ref Configurator._registered, 0);
            Interlocked.Exchange(ref GameServices.LoadClaimed, 0);
            // UnloadClaimed stays at 1 — the next session's Load() resets it at its start.
            // Do NOT reset it here: resetting here allows Unload #2 to see 0 and also win,
            // which causes _registered to be reset a second time mid-session-transition,
            // letting Configure #2 also register GSI and creating duplicate Load() calls.
            ModLog.Info("GameServicesInitializer.Unload() — complete, ready for next session");
        }

        // ── Event handlers ────────────────────────────────────────────────────

        [OnEvent]
        public void OnCharacterCreated(CharacterCreatedEvent e)
        {
            try
            {
                var beaver = e.Character.GetComponent<Beaver>();
                if (beaver == null) return;
                string name = "(unknown)";
                try { name = e.Character.FirstName ?? "(no name)"; } catch { }
                lock (GameServices.BeaversLock)
                {
                    if (!GameServices.Beavers.Contains(beaver))
                    {
                        GameServices.Beavers.Add(beaver);
                        ModLog.Info("Beaver added: \"" + name + "\" — total: " + GameServices.Beavers.Count);
                    }
                    else
                    {
                        ModLog.Warn("Beaver \"" + name + "\" already in list (duplicate event?) — skipped");
                    }
                }
            }
            catch (Exception ex) { ModLog.Error("OnCharacterCreated handler threw", ex); }
        }

        [OnEvent]
        public void OnCharacterKilled(CharacterKilledEvent e)
        {
            try
            {
                var beaver = e.Character.GetComponent<Beaver>();
                if (beaver == null) return;
                string name = "(unknown)";
                try { name = e.Character.FirstName ?? "(no name)"; } catch { }
                lock (GameServices.BeaversLock)
                {
                    bool removed = GameServices.Beavers.Remove(beaver);
                    if (removed)
                        ModLog.Info("Beaver removed: \"" + name + "\" — remaining: " + GameServices.Beavers.Count);
                    else
                        ModLog.Warn("Beaver \"" + name + "\" not found in list on kill event");
                }
            }
            catch (Exception ex) { ModLog.Error("OnCharacterKilled handler threw", ex); }
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void ResetState()
        {
            if (GameServices.EventBus != null && GameServices.RegisteredInitializer != null)
            {
                try
                {
                    GameServices.EventBus.Unregister(GameServices.RegisteredInitializer);
                    ModLog.Info("Unregistered old GameServicesInitializer from EventBus");
                }
                catch (Exception ex)
                {
                    ModLog.Warn("EventBus.Unregister threw (scene may already be torn down): " + ex.Message);
                }
            }

            lock (GameServices.BeaversLock)
            {
                ModLog.Info("Clearing beaver list (" + GameServices.Beavers.Count + " entries)");
                GameServices.Beavers.Clear();
            }

            GameServices.DayNightCycle    = null;
            GameServices.GameCycle        = null;
            GameServices.Weather          = null;
            GameServices.Hazardous        = null;
            GameServices.Population       = null;
            GameServices.BeaverPopulation = null;
            GameServices.SettlementRef    = null;
            GameServices.EventBus         = null;
            GameServices.Intermediary     = null;
            GameServices.WeatherCycle     = null;
            GameServices.AutomatorReg     = null;
            GameServices.WellbeingSvc     = null;
            GameServices.WellbeingLimit   = null;
            GameServices.Repo             = null;
            GameServices.RegisteredInitializer = null;
            GameServices.Ready            = false;
        }

        private static void Assign(object s, List<string> found)
        {
            if (s == null) return;
            var name = s.GetType().Name;
            if (!found.Contains(name)) found.Add(name);

            switch (name)
            {
                case "DayNightCycle":
                    if (GameServices.DayNightCycle == null)
                        GameServices.DayNightCycle = s as IDayNightCycle; break;
                case "GameCycleService":
                    if (GameServices.GameCycle == null)
                        GameServices.GameCycle = s as GameCycleService; break;
                case "WeatherService":
                case "ModdableWeatherService":          // game update renamed the singleton
                    if (GameServices.Weather == null)
                        GameServices.Weather = s as WeatherService; break;
                case "HazardousWeatherService":
                case "ModdableHazardousWeatherService": // game update renamed the singleton
                    if (GameServices.Hazardous == null)
                        GameServices.Hazardous = s as HazardousWeatherService; break;
                case "PopulationService":
                    if (GameServices.Population == null)
                        GameServices.Population = s as PopulationService; break;
                case "SettlementReferenceService":
                    if (GameServices.SettlementRef == null)
                        GameServices.SettlementRef = s as SettlementReferenceService; break;
                case "EventBus":
                    if (GameServices.EventBus == null)
                        GameServices.EventBus = s as EventBus; break;
                case "HttpApiIntermediary":
                    // Internal type — stored as object, accessed via reflection
                    if (GameServices.Intermediary == null)
                        GameServices.Intermediary = s; break;
                case "WeatherCycleService":
                    // ModdableWeathers mod — stored as object, accessed via reflection
                    if (GameServices.WeatherCycle == null)
                        GameServices.WeatherCycle = s; break;
                case "AutomatorRegistry":
                    if (GameServices.AutomatorReg == null)
                        GameServices.AutomatorReg = s; break;
                case "WellbeingService":
                    if (GameServices.WellbeingSvc == null)
                        GameServices.WellbeingSvc = s as WellbeingService; break;
                case "WellbeingLimitService":
                    if (GameServices.WellbeingLimit == null)
                        GameServices.WellbeingLimit = s as WellbeingLimitService; break;
                case "BeaverPopulation":
                    if (GameServices.BeaverPopulation == null)
                        GameServices.BeaverPopulation = s as BeaverPopulation; break;
            }
        }
    }
}
