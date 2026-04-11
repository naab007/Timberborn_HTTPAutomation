using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// GET  /api/automation?save=&lt;saveName&gt;
    ///   Returns the stored automation rule JSON for that save, or {"rules":[]} if none.
    ///
    /// POST /api/automation?save=&lt;saveName&gt;
    ///   Writes the request body (JSON string) to automation_saves/&lt;saveName&gt;.json.
    ///   Returns {"ok":true} on success.
    ///
    /// Both routes return 503 if the game is not yet loaded.
    /// </summary>
    public class AutomationStorageEndpoint : IHttpApiEndpoint
    {
        private const string Route = "/api/automation";

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.Equals(Route, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);

            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";
            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }

            if (!GameServices.Ready)
            {
                await HttpResponseHelper.WriteError(ctx, 503, "Game not loaded yet");
                return true;
            }

            // Extract ?save= query param
            var query = ctx.Request.Url?.Query ?? "";
            var saveName = ExtractQueryParam(query, "save");

            if (string.IsNullOrEmpty(saveName))
            {
                await HttpResponseHelper.WriteError(ctx, 400, "Missing 'save' query parameter");
                return true;
            }

            // Sanitise: strip characters that are illegal in Windows file names
            var safeFileName = SanitiseFileName(saveName) + ".json";
            var dir = Path.Combine(Plugin.ModDirectory, "automation_saves");

            try
            {
                if (method == "GET")
                {
                    var filePath = Path.Combine(dir, safeFileName);
                    if (!File.Exists(filePath))
                    {
                        ModLog.Info("AutomationStorage GET \"" + saveName + "\" — no file yet, returning empty rules");
                        await HttpResponseHelper.WriteJson(ctx, 200, "{\"rules\":[]}");
                        return true;
                    }
                    var content = File.ReadAllText(filePath, Encoding.UTF8);
                    ModLog.Info("AutomationStorage GET \"" + saveName + "\" — served " + content.Length + " chars from " + filePath);
                    await HttpResponseHelper.WriteJson(ctx, 200, content);
                }
                else if (method == "POST")
                {
                    string body;
                    using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();

                    if (string.IsNullOrWhiteSpace(body))
                    {
                        ModLog.Warn("AutomationStorage POST \"" + saveName + "\" — empty body, rejected");
                        await HttpResponseHelper.WriteError(ctx, 400, "Empty request body");
                        return true;
                    }

                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                        ModLog.Info("AutomationStorage created directory: " + dir);
                    }

                    var filePath = Path.Combine(dir, safeFileName);
                    File.WriteAllText(filePath, body, Encoding.UTF8);
                    ModLog.Info("AutomationStorage POST \"" + saveName + "\" — wrote " + body.Length + " chars to " + filePath);
                    await HttpResponseHelper.WriteJson(ctx, 200, "{\"ok\":true}");
                }
                else
                {
                    ModLog.Warn("AutomationStorage — unsupported method: " + method);
                    await HttpResponseHelper.WriteError(ctx, 405, "Use GET or POST");
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("AutomationStorage " + method + " \"" + saveName + "\" threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }

            return true;
        }

        private static string ExtractQueryParam(string query, string key)
        {
            // query is like "?save=MySave&foo=bar"
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

        private static string SanitiseFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.Length > 0 ? sb.ToString() : "unnamed";
        }
    }
}
