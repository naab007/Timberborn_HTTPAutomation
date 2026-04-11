using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// GET  /api/log?lines=N
    ///   Returns the last N lines of debug.log (backend log) as JSON.
    ///   Default N=200, max N=2000.
    ///   Response: { "lines": [...], "totalLines": N, "file": "path" }
    ///
    /// POST /api/log
    ///   Accepts a plain-text or JSON body from the frontend and writes it
    ///   to ui_log.txt in the mod folder. Used by the frontend "Save log" button.
    ///   Response: { "ok": true }
    /// </summary>
    public class LogEndpoint : IHttpApiEndpoint
    {
        private const string Route = "/api/log";

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.Equals(Route, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);

            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }

            if (method == "GET")
            {
                await HandleGet(ctx);
                return true;
            }

            if (method == "POST")
            {
                await HandlePost(ctx);
                return true;
            }

            await HttpResponseHelper.WriteError(ctx, 405, "Use GET or POST");
            return true;
        }

        // ── GET — serve tail of debug.log ────────────────────────────────────

        private static async Task HandleGet(HttpListenerContext ctx)
        {
            var query = ctx.Request.Url?.Query ?? "";
            var linesParam = ExtractQueryParam(query, "lines");
            int requestedLines = 200;
            if (!string.IsNullOrEmpty(linesParam) && int.TryParse(linesParam, out var parsed))
                requestedLines = Math.Max(1, Math.Min(2000, parsed));

            var logPath = Path.Combine(Plugin.ModDirectory, "debug.log");

            try
            {
                if (!File.Exists(logPath))
                {
                    await WriteLogResponse(ctx, new string[0], 0, logPath);
                    return;
                }

                string[] allLines;
                lock (ModLog.FileLock)
                    allLines = File.ReadAllLines(logPath, Encoding.UTF8);

                int total = allLines.Length;
                int skip  = Math.Max(0, total - requestedLines);
                var tail  = new string[total - skip];
                Array.Copy(allLines, skip, tail, 0, tail.Length);

                await WriteLogResponse(ctx, tail, total, logPath);
            }
            catch (Exception ex)
            {
                ModLog.Error("LogEndpoint GET threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }
        }

        private static async Task WriteLogResponse(HttpListenerContext ctx,
            string[] lines, int totalLines, string filePath)
        {
            var sb = new StringBuilder("{\"lines\":[");
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(HttpResponseHelper.Escape(lines[i]));
                sb.Append('"');
            }
            sb.Append("],\"totalLines\":").Append(totalLines)
              .Append(",\"file\":\"").Append(HttpResponseHelper.Escape(filePath)).Append("\"}");

            await HttpResponseHelper.WriteJson(ctx, 200, sb.ToString());
        }

        // ── POST — save frontend UI log to ui_log.txt ─────────────────────────

        private static async Task HandlePost(HttpListenerContext ctx)
        {
            try
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    body = reader.ReadToEnd();

                var uiLogPath = Path.Combine(Plugin.ModDirectory, "ui_log.txt");
                var stamp     = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                File.WriteAllText(uiLogPath,
                    "=== Saved " + stamp + " ===\r\n" + body + "\r\n",
                    Encoding.UTF8);

                ModLog.Info("LogEndpoint POST: wrote " + body.Length + " chars to ui_log.txt");
                await HttpResponseHelper.WriteJson(ctx, 200, "{\"ok\":true}");
            }
            catch (Exception ex)
            {
                ModLog.Error("LogEndpoint POST threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string ExtractQueryParam(string query, string key)
        {
            if (string.IsNullOrEmpty(query)) return "";
            var raw = query.TrimStart('?');
            foreach (var part in raw.Split('&'))
            {
                var idx = part.IndexOf('=');
                if (idx < 0) continue;
                var k = Uri.UnescapeDataString(part.Substring(0, idx));
                var v = Uri.UnescapeDataString(part.Substring(idx + 1));
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return v;
            }
            return "";
        }
    }
}
