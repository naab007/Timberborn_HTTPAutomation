using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace HTTPAutomation
{
    internal static class HttpResponseHelper
    {
        public static void AddCorsHeaders(HttpListenerResponse r)
        {
            r.Headers.Set("Access-Control-Allow-Origin",  "*");
            r.Headers.Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            r.Headers.Set("Access-Control-Allow-Headers", "Content-Type");
            r.Headers.Set("Cache-Control", "no-store, no-cache, must-revalidate");
            r.Headers.Set("Pragma",        "no-cache");
        }

        public static async Task WriteJson(HttpListenerContext ctx, int status, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode      = status;
            ctx.Response.ContentType     = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }

        public static async Task WriteError(HttpListenerContext ctx, int status, string msg)
            => await WriteJson(ctx, status, "{\"error\":\"" + Escape(msg) + "\"}");

        public static Task WriteOptions(HttpListenerContext ctx)
        {
            ctx.Response.StatusCode = 204;
            ctx.Response.Close();
            return Task.FromResult(0);
        }

        public static string Escape(string s) =>
            (s ?? "")
                .Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

        public static string Fmt(float v)  => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
        public static string Bool(bool v)  => v ? "true" : "false";
    }
}
