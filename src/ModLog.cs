using System;
using System.IO;
using System.Text;

namespace HTTPAutomation
{
    /// <summary>
    /// Shared, thread-safe logger for all HTTPAutomation subsystems.
    /// Writes timestamped, levelled lines to debug.log in the mod directory.
    /// Rolls the log (truncates oldest half) when it exceeds MaxBytes.
    /// </summary>
    internal static class ModLog
    {
        private static readonly object  _lock    = new object();
        // Exposed so LogEndpoint can share the same lock when reading the file
        internal static object FileLock => _lock;
        private const  long             MaxBytes = 512 * 1024; // 500 KB
        private static string           _path;

        // ── Public API ────────────────────────────────────────────────────────

        public static void Info (string msg)              => Write("INFO ", msg);
        public static void Warn (string msg)              => Write("WARN ", msg);
        public static void Error(string msg)              => Write("ERROR", msg);
        public static void Error(string msg, Exception ex) => Write("ERROR", msg + "\n  " + FormatEx(ex));

        // ── Internals ─────────────────────────────────────────────────────────

        private static string GetPath()
        {
            if (_path != null) return _path;
            _path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Timberborn", "Mods", "HTTPAutomation", "debug.log");
            return _path;
        }

        private static void Write(string level, string msg)
        {
            try
            {
                var path = GetPath();
                var line = DateTime.Now.ToString("HH:mm:ss.fff") + " [" + level + "] " + msg + "\n";
                var bytes = Encoding.UTF8.GetBytes(line);

                lock (_lock)
                {
                    // Roll if over cap: keep the second half of the file
                    try
                    {
                        var fi = new FileInfo(path);
                        if (fi.Exists && fi.Length > MaxBytes)
                        {
                            var all  = File.ReadAllBytes(path);
                            var half = all.Length / 2;
                            // Find next newline after half-point so we don't cut mid-line
                            while (half < all.Length && all[half] != (byte)'\n') half++;
                            half++;
                            var kept = new byte[all.Length - half];
                            Array.Copy(all, half, kept, 0, kept.Length);
                            File.WriteAllBytes(path, kept);
                            File.AppendAllText(path, "--- log rolled ---\n");
                        }
                    }
                    catch { /* never let roll failure suppress the actual message */ }

                    using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        fs.Write(bytes, 0, bytes.Length);
                }
            }
            catch { /* logging must never throw into caller */ }
        }

        private static string FormatEx(Exception ex)
        {
            if (ex == null) return "(null exception)";
            var sb = new StringBuilder();
            sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            if (!string.IsNullOrEmpty(ex.StackTrace))
                sb.Append("\n  ").Append(ex.StackTrace.Replace("\n", "\n  "));
            if (ex.InnerException != null)
                sb.Append("\n  caused by: ").Append(FormatEx(ex.InnerException));
            return sb.ToString();
        }
    }
}
