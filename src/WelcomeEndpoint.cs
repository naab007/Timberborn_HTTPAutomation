using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// GET /api/welcome
    /// Reads welcome.json from the mod directory.
    /// Returns {"title":"...","text":"..."} with HTTP 200 if the file exists.
    /// Returns HTTP 404 if the file does not exist (frontend ignores 404 silently).
    ///
    /// To disable the welcome popup: delete welcome.json from the mod folder.
    ///
    /// welcome.json format:
    /// {
    ///   "_comment": "Delete this file to remove the welcome popup",
    ///   "title": "Welcome",
    ///   "text": "Your message here.\nSupports newlines."
    /// }
    /// </summary>
    public class WelcomeEndpoint : IHttpApiEndpoint
    {
        private static readonly string Route = "/api/welcome";

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path   = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

            if (!path.Equals(Route, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);

            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }
            if (method != "GET")     { await HttpResponseHelper.WriteError(ctx, 405, "Use GET"); return true; }

            try
            {
                var filePath = Path.Combine(Plugin.ModDirectory, "welcome.json");

                if (!File.Exists(filePath))
                {
                    // 404 — no welcome file, frontend silently ignores this
                    await HttpResponseHelper.WriteError(ctx, 404, "No welcome.json");
                    return true;
                }

                var raw = File.ReadAllText(filePath);

                // Parse out title and text with minimal JSON extraction
                // (avoids adding a JSON library dependency)
                var title = ExtractJsonString(raw, "title") ?? "Welcome";
                var text  = ExtractJsonString(raw, "text")  ?? "";

                var json = "{\"title\":\"" + HttpResponseHelper.Escape(title)
                         + "\",\"text\":\""  + HttpResponseHelper.Escape(text) + "\"}";

                await HttpResponseHelper.WriteJson(ctx, 200, json);
            }
            catch (Exception ex)
            {
                ModLog.Error("/api/welcome threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }

            return true;
        }

        /// <summary>
        /// Minimal JSON string field extractor. Finds "key":"value" and returns value.
        /// Handles escaped characters properly — sufficient for a simple welcome.json.
        /// </summary>
        private static string ExtractJsonString(string json, string key)
        {
            // Find:  "key"   :   "
            var searchKey = "\"" + key + "\"";
            int ki = json.IndexOf(searchKey, StringComparison.Ordinal);
            if (ki < 0) return null;

            int colon = json.IndexOf(':', ki + searchKey.Length);
            if (colon < 0) return null;

            // Skip whitespace after colon
            int start = colon + 1;
            while (start < json.Length && (json[start] == ' ' || json[start] == '\t'
                    || json[start] == '\r' || json[start] == '\n'))
                start++;

            if (start >= json.Length || json[start] != '"') return null;
            start++; // skip opening quote

            // Read until unescaped closing quote
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    char next = json[++i];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(next); break;
                    }
                }
                else if (c == '"')
                {
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
            }
            return null; // unterminated string
        }
    }
}
