using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// GET /api/sensors
    /// Returns all automation transmitter buildings (sensors) with their current state.
    ///
    /// Uses AutomatorRegistry.Transmitters (reflected) to enumerate every sensor
    /// placed in the map. Each Automator component gives us:
    ///   - AutomatorName  → user-assigned building name
    ///   - AutomatorId    → stable GUID string (session-persistent)
    ///   - State          → AutomatorState enum (mapped to bool isOn)
    ///   - GetComponents() → iterated to detect sensor type from component class names
    ///
    /// Numeric measurement values (flow rate, depth, contamination %) are stored in
    /// internal component types and are not yet accessible — they appear as null.
    /// The isOn boolean reflects the sensor's current output signal, which is what
    /// the game's own automation system and our frontend rule engine use.
    /// </summary>
    public class SensorEndpoint : IHttpApiEndpoint
    {
        private const string SensorsRoute = "/api/sensors";

        // Reflection cache — resolved once on first successful call
        private bool         _resolved;
        private PropertyInfo _transmittersProp;   // AutomatorRegistry.Transmitters
        private PropertyInfo _automatorsProp;      // AutomatorRegistry.Automators (all, for diagnostics)
        private PropertyInfo _nameProp;            // Automator.AutomatorName    (resolved via interface)
        private PropertyInfo _idProp;              // Automator.AutomatorId      (resolved via interface)
        private PropertyInfo _stateProp;           // Automator.State            (resolved via interface)
        private PropertyInfo _allComponentsProp;   // Automator.AllComponents    (resolved via interface)
        private bool         _membersCached;       // true once members resolved

        // One-shot probe: dump automator structure on first automator seen.
        private bool _probed;
        // Per-sensor-type probe: dump component members on first occurrence of each sensor type.
        private readonly HashSet<string> _probedComponentTypes = new HashSet<string>();

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path   = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

            if (!path.Equals(SensorsRoute, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);
            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }
            if (method != "GET")     { await HttpResponseHelper.WriteError(ctx, 405, "Use GET"); return true; }

            if (!GameServices.Ready)
            { await HttpResponseHelper.WriteError(ctx, 503, "Game not loaded yet"); return true; }

            try
            {
                var json = BuildSensorsJson();
                await HttpResponseHelper.WriteJson(ctx, 200, json);
            }
            catch (Exception ex)
            {
                ModLog.Error("/api/sensors threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }
            return true;
        }

        private string BuildSensorsJson()
        {
            var reg = GameServices.AutomatorReg;
            if (reg == null)
            {
                ModLog.Warn("SensorEndpoint: AutomatorReg is null — AutomatorRegistry not found in singleton repo");
                return "{\"sensors\":[],\"error\":\"AutomatorRegistry not resolved — no save loaded or DLL not ready\"}";
            }

            EnsureReflection(reg);
            if (_transmittersProp == null)
                return "{\"sensors\":[],\"error\":\"AutomatorRegistry.Transmitters property not found\"}";

            var transmitters = _transmittersProp.GetValue(reg);
            if (transmitters == null)
            {
                ModLog.Warn("SensorEndpoint: Transmitters value is null");
                return "{\"sensors\":[]}";
            }

            var enumerable = transmitters as IEnumerable;
            if (enumerable == null)
            {
                ModLog.Warn("SensorEndpoint: Transmitters is not IEnumerable — actual type: " + transmitters.GetType().FullName);
                return "{\"sensors\":[],\"error\":\"Transmitters is not enumerable\"}";
            }

            // Count transmitters for diagnostics
            int count = 0;
            var countEnum = enumerable.GetEnumerator();
            while (countEnum.MoveNext()) count++;
            ModLog.Info("SensorEndpoint: Transmitters count = " + count);

            if (count == 0 && _automatorsProp != null)
            {
                // Log all-automators count to distinguish "no sensors placed" from "wrong collection"
                var allAutos = _automatorsProp.GetValue(reg) as IEnumerable;
                int allCount = 0;
                if (allAutos != null) foreach (var _ in allAutos) allCount++;
                ModLog.Info("SensorEndpoint: Automators (all) count = " + allCount
                    + " — if > 0, sensors may not be tagged as Transmitters");
            }

            var sb = new StringBuilder("{\"sensors\":[");
            bool first = true;

            foreach (var automator in enumerable)
            {
                if (automator == null) continue;
                try
                {
                    var entry = ReadAutomator(automator);
                    if (entry == null) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(entry);
                }
                catch (Exception ex)
                {
                    ModLog.Warn("SensorEndpoint: failed to read automator ("
                        + automator.GetType().FullName + ") — " + ex.Message);
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private string ReadAutomator(object automator)
        {
            var t = automator.GetType();

            // One-shot diagnostic: log type flags and interface list before any reflection.
            // Timberborn.Automation.Automator can report ContainsGenericParameters=true in
            // Mono/Unity due to generic methods on the class — even though the class itself
            // is not generic. PropertyInfo.GetValue() / MethodInfo.Invoke() from the concrete
            // type both throw in that case. The fix: resolve properties from the interfaces
            // the type implements; interface PropertyInfo is non-generic and GetValue works.
            if (!_probed)
            {
                ModLog.Info("SensorEndpoint: Automator type=" + t.FullName
                    + " IsGenericType=" + t.IsGenericType
                    + " IsGenericTypeDef=" + t.IsGenericTypeDefinition
                    + " ContainsGenericParams=" + t.ContainsGenericParameters);
                var ifaces = t.GetInterfaces();
                var ifaceSb = new StringBuilder("SensorEndpoint: Automator interfaces: ");
                foreach (var iface in ifaces) ifaceSb.Append(iface.Name).Append(", ");
                ModLog.Info(ifaceSb.ToString());
            }

            // Lazily resolve per-automator properties/methods on first call.
            // Use FindInterfaceProp() so the resolved PropertyInfo comes from a non-generic
            // interface and is immune to the ContainsGenericParameters Mono quirk.
            if (!_membersCached)
            {
                _membersCached = true;
                _nameProp  = FindInterfaceProp(t, "AutomatorName");
                _idProp    = FindInterfaceProp(t, "AutomatorId");
                _stateProp = FindInterfaceProp(t, "State");

                ModLog.Info("SensorEndpoint: AutomatorName prop="
                    + (_nameProp  != null ? _nameProp.DeclaringType?.Name  + "." + _nameProp.Name  : "NOT FOUND"));
                ModLog.Info("SensorEndpoint: AutomatorId prop="
                    + (_idProp    != null ? _idProp.DeclaringType?.Name    + "." + _idProp.Name    : "NOT FOUND"));
                ModLog.Info("SensorEndpoint: State prop="
                    + (_stateProp != null ? _stateProp.DeclaringType?.Name + "." + _stateProp.Name : "NOT FOUND"));

                // AllComponents — use this instead of GetComponents() method.
                // GetComponents() on Automator returns null (likely a generic method or context issue).
                // AllComponents IS present as a ReadOnlyList<BaseComponent> property and works fine
                // when resolved via interface to avoid ContainsGenericParameters.
                _allComponentsProp = FindInterfaceProp(t, "AllComponents");
                ModLog.Info("SensorEndpoint: AllComponents prop="
                    + (_allComponentsProp != null
                        ? _allComponentsProp.DeclaringType?.Name + "." + _allComponentsProp.Name
                        : "NOT FOUND"));
            }

            var name  = _nameProp  != null ? SafeGetValue(_nameProp,  automator) as string ?? "" : "";
            var id    = _idProp    != null ? SafeGetValue(_idProp,    automator)?.ToString() ?? "" : "";
            var state = _stateProp != null ? SafeGetValue(_stateProp, automator) : null;

            // AutomatorState is an enum. OFF is named "Off" or has int value 0.
            bool isOn = false;
            if (state != null)
            {
                var stateStr = state.ToString();
                isOn = stateStr != "Off"
                    && stateStr != "off"
                    && stateStr != "Undetermined"
                    && stateStr != "Unknown"
                    && stateStr != "0";
            }

            // Detect sensor type and unit by scanning GetComponents() for known types.
            string  sensorType  = "Unknown";
            string  unit        = "";
            object  matchedComp = null;

            // Enumerate entity components via AllComponents property (GetComponents() returns null).
            IEnumerable compEnum = null;
            if (_allComponentsProp != null)
            {
                var result = SafeGetValue(_allComponentsProp, automator);
                compEnum = result as IEnumerable;
            }

            if (compEnum != null)
            {
                foreach (var comp in compEnum)
                {
                    if (comp == null) continue;
                    var typeName = comp.GetType().Name;
                    if (TryGetSensorType(typeName, out var detectedType, out var detectedUnit))
                    {
                        sensorType  = detectedType;
                        unit        = detectedUnit;
                        matchedComp = comp;
                        break;
                    }
                }
            }

            // One-shot automator structure probe (fires on the very first automator seen).
            if (!_probed)
            {
                _probed = true;
                ProbeAutomator(automator, compEnum);
            }

            // Per-type component probe: fires once per distinct sensor type.
            // This is separate from the automator probe so we get data even if the
            // first automator happens to be a non-sensor (e.g. HTTP lever).
            if (matchedComp != null && !_probedComponentTypes.Contains(sensorType))
            {
                _probedComponentTypes.Add(sensorType);
                ProbeComponent(sensorType, matchedComp);
            }

            // Try to read the current numeric measurement and configured threshold/operator.
            // These live in internal component types; we probe known property/field names.
            // If inaccessible the fields stay null and the frontend falls back to isOn.
            float?  numericValue = TryReadFloat(matchedComp, new[] {
                // Generic names (kept for forward-compat)
                "Value", "CurrentValue", "Level", "CurrentLevel", "Measurement",
                // DepthSensor
                "Depth", "DepthFromFloor", "WaterLevel",
                // ContaminationSensor (Nullable<Single>)
                "SampledContamination", "ContaminationLevel",
                // FlowSensor (Nullable<Single>)
                "SampledFlow", "FlowRate",
                // Chronometer
                "SampledTime",
                // ScienceCounter
                "SampledSciencePoints",
                // ResourceCounter
                "SampledResourceCount", "SampledFillRate",
                // PowerMeter
                "IntMeasurement", "PercentMeasurement",
                // fallback
                "Count", "Amount", "Power", "Science", "Score",
                // Logic components (Relay/Memory/Timer) — return mode enum as int
                "Mode" });
            float?  threshold    = TryReadFloat(matchedComp, new[] {
                "Threshold", "IntThreshold", "PercentThreshold", "MinThreshold",
                "FillRateThreshold", "TriggerLevel", "TriggerValue",
                "ComparisonThreshold", "Target", "Limit", "DesiredValue" });
            string  operatorStr  = TryReadOperatorStr(matchedComp);

            // Build JSON entry
            return "{\"id\":\""        + HttpResponseHelper.Escape(id)
                + "\",\"name\":\""     + HttpResponseHelper.Escape(name)
                + "\",\"type\":\""     + HttpResponseHelper.Escape(sensorType)
                + "\",\"unit\":\""     + HttpResponseHelper.Escape(unit)
                + "\",\"isOn\":"       + HttpResponseHelper.Bool(isOn)
                + ",\"value\":"        + FormatFloat(numericValue)
                + ",\"threshold\":"    + FormatFloat(threshold)
                + ",\"operator\":"     + (operatorStr != null
                                            ? "\"" + HttpResponseHelper.Escape(operatorStr) + "\""
                                            : "null")
                + "}";
        }

        private static bool TryGetSensorType(string componentTypeName,
                                               out string type, out string unit)
        {
            // Skip blueprint Spec components — they hold static config only (SensorCoordinates, etc).
            // We want the runtime component (e.g. DepthSensor, not DepthSensorSpec) which holds
            // the live measurement. The AllComponents list includes both; skip Spec to hit runtime.
            if (componentTypeName.EndsWith("Spec")) { type = null; unit = null; return false; }

            // Match against known runtime component type name fragments.
            if (componentTypeName.Contains("FlowSensor"))
            { type = "FlowSensor";           unit = "m³/s";   return true; }
            if (componentTypeName.Contains("DepthSensor"))
            { type = "DepthSensor";          unit = "m";      return true; }
            if (componentTypeName.Contains("ContaminationSensor"))
            { type = "ContaminationSensor";  unit = "%";      return true; }
            if (componentTypeName.Contains("Chronometer"))
            { type = "Chronometer";          unit = "HH:MM";  return true; }
            if (componentTypeName.Contains("WeatherStation"))
            { type = "WeatherStation";       unit = "";       return true; }
            if (componentTypeName.Contains("PowerMeter"))
            { type = "PowerMeter";           unit = "HP";     return true; }
            if (componentTypeName.Contains("PopulationCounter"))
            { type = "PopulationCounter";    unit = "beavers"; return true; }
            if (componentTypeName.Contains("ResourceCounter"))
            { type = "ResourceCounter";      unit = "goods";  return true; }
            if (componentTypeName.Contains("ScienceCounter"))
            { type = "ScienceCounter";       unit = "pts";    return true; }
            if (componentTypeName.Contains("Memory"))
            { type = "Memory";               unit = "mode";   return true; }
            if (componentTypeName.Contains("Relay"))
            { type = "Relay";                unit = "mode";   return true; }
            if (componentTypeName.Contains("Timer") && !componentTypeName.Contains("TimerModel"))
            { type = "Timer";                unit = "mode";   return true; }

            type = null; unit = null; return false;
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Dumps the automator's own public properties and all method names,
        /// plus all component type names from GetComponents. Runs once per session.
        /// </summary>
        private static void ProbeAutomator(object automator, IEnumerable components)
        {
            try
            {
                var t = automator.GetType();
                ModLog.Info("=== SensorEndpoint PROBE: Automator type = " + t.FullName + " ===");

                var flags = BindingFlags.Public | BindingFlags.Instance;

                // Log interfaces (helps debug ContainsGenericParameters issues)
                var ifaces = t.GetInterfaces();
                var ifaceSb2 = new System.Text.StringBuilder("  Interfaces (" + ifaces.Length + "): ");
                foreach (var iface in ifaces) ifaceSb2.Append(iface.Name).Append(", ");
                ModLog.Info(ifaceSb2.ToString());

                // Properties — use FindInterfaceProp-resolved props to avoid ContainsGenericParameters
                // errors; also try raw t.GetProperties for completeness (wrapped in try/catch).
                var props = t.GetProperties(flags);
                ModLog.Info("  Properties (" + props.Length + "):");
                foreach (var p in props)
                {
                    try
                    {
                        // Prefer interface-safe resolution for the actual GetValue call
                        var safeProp = FindInterfaceProp(t, p.Name) ?? p;
                        var v = SafeGetValue(safeProp, automator);
                        ModLog.Info("    PROP " + p.PropertyType.Name + " " + p.Name
                            + " = " + (v != null ? v.ToString() : "null"));
                    }
                    catch (Exception ex)
                    {
                        ModLog.Info("    PROP " + p.PropertyType.Name + " " + p.Name
                            + " [error: " + ex.Message + "]");
                    }
                }

                // Methods (names only)
                var methods = t.GetMethods(flags);
                var methodNames = new System.Text.StringBuilder("  Methods: ");
                foreach (var m in methods)
                    methodNames.Append(m.Name).Append(", ");
                ModLog.Info(methodNames.ToString());

                // Component type names
                if (components != null)
                {
                    var compTypes = new System.Text.StringBuilder("  Components: ");
                    foreach (var c in components)
                        if (c != null) compTypes.Append(c.GetType().Name).Append(", ");
                    ModLog.Info(compTypes.ToString());
                }
                else
                {
                    ModLog.Info("  Components: (GetComponents returned null)");
                }

                ModLog.Info("=== END Automator PROBE ===");
            }
            catch (Exception ex)
            {
                ModLog.Error("SensorEndpoint.ProbeAutomator threw", ex);
            }
        }

        /// <summary>
        /// Dumps all properties and fields (public + non-public) of a sensor component
        /// to debug.log so we can discover the correct member names for value/threshold/op.
        /// Called when a matched sensor component is found.
        /// </summary>
        private static void ProbeComponent(string sensorTypeName, object comp)
        {
            try
            {
                var t = comp.GetType();
                ModLog.Info("=== SensorEndpoint PROBE: " + sensorTypeName
                    + " / " + t.FullName + " ===");

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (var prop in t.GetProperties(flags))
                {
                    try
                    {
                        var val = prop.GetValue(comp);
                        ModLog.Info("  PROP " + prop.PropertyType.Name + " " + prop.Name
                            + " = " + (val != null ? val.ToString() : "null"));
                    }
                    catch (Exception ex)
                    {
                        ModLog.Info("  PROP " + prop.PropertyType.Name + " " + prop.Name
                            + " [error: " + ex.Message + "]");
                    }
                }

                foreach (var field in t.GetFields(flags))
                {
                    try
                    {
                        var val = field.GetValue(comp);
                        ModLog.Info("  FIELD " + field.FieldType.Name + " " + field.Name
                            + " = " + (val != null ? val.ToString() : "null"));
                    }
                    catch (Exception ex)
                    {
                        ModLog.Info("  FIELD " + field.FieldType.Name + " " + field.Name
                            + " [error: " + ex.Message + "]");
                    }
                }

                ModLog.Info("=== END PROBE ===");
            }
            catch (Exception ex)
            {
                ModLog.Error("SensorEndpoint.ProbeComponent threw", ex);
            }
        }

        /// <summary>
        /// Tries to read a float/double/int value from a named property or field.
        /// Searches both public and non-public members to reach backing fields.
        /// Returns null if none of the names match or the value is not numeric.
        /// </summary>
        private static float? TryReadFloat(object comp, string[] names)
        {
            if (comp == null) return null;
            var t = comp.GetType();
            foreach (var name in names)
            {
                try
                {
                    var prop = t.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null)
                    {
                        var pt = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                        if (pt == typeof(float) || pt == typeof(double) ||
                            pt == typeof(int)   || pt == typeof(long))
                        {
                            var val = prop.GetValue(comp);
                            if (val != null) return Convert.ToSingle(val);
                        }
                        else if (pt.IsEnum)
                        {
                            var val = prop.GetValue(comp);
                            if (val != null) return (float)Convert.ToInt32(val);
                        }
                    }
                    var field = t.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var ft = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                        if (ft == typeof(float) || ft == typeof(double) ||
                            ft == typeof(int)   || ft == typeof(long))
                        {
                            var val = field.GetValue(comp);
                            if (val != null) return Convert.ToSingle(val);
                        }
                        else if (ft.IsEnum)
                        {
                            var val = field.GetValue(comp);
                            if (val != null) return (float)Convert.ToInt32(val);
                        }
                    }
                }
                catch { /* ignore inaccessible members */ }
            }
            return null;
        }

        /// <summary>
        /// Tries to read a comparison-operator enum from the component and normalise it
        /// to the frontend's gt/gte/lt/lte/eq strings.
        /// </summary>
        private static string TryReadOperatorStr(object comp)
        {
            if (comp == null) return null;
            var t = comp.GetType();
            foreach (var name in new[] { "Mode", "ComparisonMode", "Operator", "ComparisonOperator", "ComparisonType", "Op" })
            {
                try
                {
                    var prop = t.GetProperty(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.PropertyType.IsEnum)
                    {
                        var val = prop.GetValue(comp);
                        if (val != null) return NormalizeOperator(val.ToString());
                    }
                    var field = t.GetField(name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null && field.FieldType.IsEnum)
                    {
                        var val = field.GetValue(comp);
                        if (val != null) return NormalizeOperator(val.ToString());
                    }
                }
                catch { }
            }
            return null;
        }

        private static string NormalizeOperator(string enumStr)
        {
            if (string.IsNullOrEmpty(enumStr)) return null;
            var s = enumStr.ToLowerInvariant();
            // NumericComparisonMode values: Greater, Less, GreaterOrEqual, LessOrEqual, Equal
            if (s == "greaterorequal" || s == "greaterorequals") return "gte";
            if (s == "lessorequal"    || s == "lessorequals")    return "lte";
            if (s == "greater")  return "gt";
            if (s == "less")     return "lt";
            if (s == "equal" || s == "equals") return "eq";
            // Fuzzy fallback for other naming conventions
            if (s.Contains("greater") && (s.Contains("equal") || s.Contains("orequal"))) return "gte";
            if (s.Contains("greater")) return "gt";
            if (s.Contains("less") && (s.Contains("equal") || s.Contains("orequal")))    return "lte";
            if (s.Contains("less"))    return "lt";
            if (s.Contains("equal"))   return "eq";
            // Non-comparison enum (TimerMode, ChronometerMode, etc.) — return null so frontend ignores it
            return null;
        }

        private static string FormatFloat(float? value)
        {
            if (!value.HasValue) return "null";
            return value.Value.ToString("G6",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        // ── Interface-safe reflection helpers ────────────────────────────────

        /// <summary>
        /// Finds a property by searching the concrete type's interfaces FIRST.
        /// When the concrete type has ContainsGenericParameters=true (Mono quirk
        /// caused by generic methods on the class), GetValue()/Invoke() on any
        /// member obtained from that type throws. Interface PropertyInfo objects
        /// are non-generic and not affected — and GetValue() still dispatches
        /// through the vtable to the concrete implementation correctly.
        /// Falls back to the concrete type if no interface declares the property.
        /// </summary>
        private static PropertyInfo FindInterfaceProp(Type concreteType, string propName)
        {
            foreach (var iface in concreteType.GetInterfaces())
            {
                var p = iface.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (p != null && !iface.ContainsGenericParameters) return p;
            }
            // Fallback: walk base type chain for a declaring type that is not generic
            for (var bt = concreteType; bt != null && bt != typeof(object); bt = bt.BaseType)
            {
                if (bt.ContainsGenericParameters) continue;
                var p = bt.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                if (p != null) return p;
            }
            return concreteType.GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
        }

        /// <summary>
        /// Calls PropertyInfo.GetValue(); on failure falls back to invoking the
        /// getter MethodInfo directly. Logs a warning on unexpected errors.
        /// </summary>
        private static object SafeGetValue(PropertyInfo prop, object obj)
        {
            try { return prop.GetValue(obj); }
            catch (InvalidOperationException)
            {
                // ContainsGenericParameters fallback — try getter invoke directly
                try
                {
                    var getter = prop.GetGetMethod(nonPublic: true);
                    if (getter != null) return getter.Invoke(obj, null);
                }
                catch { }
                return null;
            }
        }

        private void EnsureReflection(object reg)
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var t = reg.GetType();
                ModLog.Info("SensorEndpoint: AutomatorRegistry type = " + t.FullName);

                _transmittersProp = t.GetProperty("Transmitters");
                if (_transmittersProp != null)
                    ModLog.Info("SensorEndpoint: AutomatorRegistry.Transmitters resolved OK"
                        + " (returns " + _transmittersProp.PropertyType.Name + ")");
                else
                    ModLog.Warn("SensorEndpoint: AutomatorRegistry.Transmitters not found");

                _automatorsProp = t.GetProperty("Automators");
                if (_automatorsProp != null)
                    ModLog.Info("SensorEndpoint: AutomatorRegistry.Automators resolved OK");
                else
                    ModLog.Info("SensorEndpoint: AutomatorRegistry.Automators not found (ok)");

                // Log all property names on AutomatorRegistry for diagnostics
                var propNames = new System.Text.StringBuilder("SensorEndpoint: AutomatorRegistry props: ");
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    propNames.Append(p.Name).Append(", ");
                ModLog.Info(propNames.ToString());
            }
            catch (Exception ex)
            {
                ModLog.Error("SensorEndpoint.EnsureReflection threw", ex);
            }
        }
    }
}
