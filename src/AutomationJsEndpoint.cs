using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// Serves GET /automation.js — reads HttpApi/index.hbs from the
    /// mod directory, strips the outer &lt;script&gt; tags if present, and returns raw JS.
    ///
    /// This avoids embedding the JS as a C# string literal and lets the JS file
    /// be edited without recompiling the DLL.
    /// </summary>
    public class AutomationJsEndpoint : IHttpApiEndpoint
    {
        private const string Route = "/automation.js";

        public Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (!path.StartsWith(Route, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);

            HttpResponseHelper.AddCorsHeaders(ctx.Response);

            string js;
            try
            {
                var filePath = Path.Combine(Plugin.ModDirectory, "HttpApi", "index.hbs");

                if (!File.Exists(filePath))
                {
                    ModLog.Warn("AutomationJs: JS file not found at: " + filePath);
                    js = "console.warn('HTTPAutomation: JS file not found at: " + filePath.Replace("\\", "\\\\") + "');";
                }
                else
                {
                    var raw = File.ReadAllText(filePath, Encoding.UTF8);
                    js = raw
                        .Replace("<script>", "")
                        .Replace("</script>", "")
                        .Trim();
                    ModLog.Info("AutomationJs: served " + js.Length + " chars from " + filePath);
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("AutomationJs: failed to read JS file", ex);
                js = "console.error('HTTPAutomation JS load error: " + ex.Message.Replace("'", "\\'") + "');";
            }

            var bytes = Encoding.UTF8.GetBytes(js);
            ctx.Response.StatusCode      = 200;
            ctx.Response.ContentType     = "application/javascript; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            // Prevent the browser caching the JS — changes to index-levers-footer.hbs
            // must be reflected immediately without bumping the ?v= query param each time.
            ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
            ctx.Response.Headers["Pragma"]        = "no-cache";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();

            return Task.FromResult(true);
        }
    }
}
