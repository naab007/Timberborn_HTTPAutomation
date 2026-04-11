using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// GET /api/levers
    ///   Enriched lever list. Intercepts the stock game route and returns each lever with:
    ///     name          — building name
    ///     state         — current on/off state
    ///     isSpringReturn — whether it auto-resets
    ///     switchOnUrl   — POST to this to turn the lever on
    ///     switchOffUrl  — POST to this to turn the lever off
    ///     colorUrl      — URL template: replace {color} with any 6-char hex string
    ///                     e.g. "ff0000", "3a7bff", "ffffff", "7b2cf8"
    ///                     POST to the resolved URL to set the lever indicator color
    ///                     — drives real in-game model recoloring, any hex is valid.
    ///
    ///   The game's own POST /api/color/<name>/<hex> endpoint handles execution.
    ///   We only expose the URL template so the frontend can build any color call it needs.
    ///
    ///   Returns 503 if game not loaded, 200 + JSON array on success.
    /// </summary>
    public class LeverEndpoint : IHttpApiEndpoint
    {
        private const string Route = "/api/levers";

        public async Task<bool> TryHandle(HttpListenerContext ctx)
        {
            var path   = ctx.Request.Url?.AbsolutePath ?? "";
            var method = ctx.Request.HttpMethod?.ToUpperInvariant() ?? "GET";

            // Only intercept bare /api/levers GET.
            // Subpaths like /api/levers/... are stock game routes — pass them through.
            if (!path.Equals(Route, StringComparison.OrdinalIgnoreCase))
                return false;

            HttpResponseHelper.AddCorsHeaders(ctx.Response);

            if (method == "OPTIONS") { await HttpResponseHelper.WriteOptions(ctx); return true; }
            if (method != "GET")     { await HttpResponseHelper.WriteError(ctx, 405, "Use GET"); return true; }

            if (!GameServices.Ready || GameServices.Intermediary == null)
            {
                await HttpResponseHelper.WriteError(ctx, 503, "Game not loaded yet");
                return true;
            }

            try
            {
                var levers = GameServices.GetLevers();
                var sb = new StringBuilder("[");
                bool first = true;

                foreach (var lever in levers)
                {
                    if (!first) sb.Append(',');
                    first = false;

                    // URL-encode the lever name the same way the game does:
                    // space → %20, etc. This must match the format the game's
                    // own endpoints expect when receiving the name back in a request.
                    var encoded = Uri.EscapeDataString(lever.Name);

                    var switchOnUrl  = "/api/switch-on/"  + encoded;
                    var switchOffUrl = "/api/switch-off/" + encoded;

                    // colorUrl is a template. The caller replaces {color} with any
                    // 6-char lowercase hex string before making the POST request.
                    // Examples: ff0000 (red), 00ff00 (green), 3a7bff (blue), ffffff (white)
                    // The game's own /api/color/<name>/<hex> endpoint handles the actual
                    // recoloring — no additional backend code needed on our side.
                    var colorUrl = "/api/color/" + encoded + "/{color}";

                    sb.Append("{\"name\":\"")         .Append(HttpResponseHelper.Escape(lever.Name))
                      .Append("\",\"state\":")         .Append(HttpResponseHelper.Bool(lever.State))
                      .Append(",\"isSpringReturn\":") .Append(HttpResponseHelper.Bool(lever.IsSpringReturn))
                      .Append(",\"switchOnUrl\":\"")  .Append(HttpResponseHelper.Escape(switchOnUrl))
                      .Append("\",\"switchOffUrl\":\"").Append(HttpResponseHelper.Escape(switchOffUrl))
                      .Append("\",\"colorUrl\":\"")   .Append(HttpResponseHelper.Escape(colorUrl))
                      .Append("\"}");
                }

                sb.Append(']');

                ModLog.Info("LeverEndpoint GET: returned " + levers.Length + " levers");
                await HttpResponseHelper.WriteJson(ctx, 200, sb.ToString());
            }
            catch (Exception ex)
            {
                ModLog.Error("LeverEndpoint GET threw", ex);
                await HttpResponseHelper.WriteError(ctx, 500, ex.Message);
            }

            return true;
        }
    }
}
