using System;
using Timberborn.HttpApiSystem;

namespace HTTPAutomation
{
    /// <summary>
    /// Injected as IHttpApiPageSection into the stock index.hbs template.
    /// BuildBody()   → hides stock header/main so they don't flash before our JS takeover.
    /// BuildFooter() → loads /automation.js (served by AutomationJsEndpoint).
    /// Order         → high number so we run after stock lever/adapter sections.
    /// </summary>
    public class AutomationUiSection : IHttpApiPageSection
    {
        public int Order => 1000;

        public string BuildBody()
        {
            // Hide stock game UI elements before our takeover() JS runs,
            // preventing a flash of the stock UI on page load.
            // Uses the actual selectors from the game's index.hbs template.
            return "<style>"
                 + "nav.navbar.fixed-top{display:none!important}"
                 + "main.container-fluid{display:none!important}"
                 + "header.container{display:none!important}"
                 + "main.container{display:none!important}"
                 + "body{background:#212529}"
                 + "</style>";
        }

        public string BuildFooter()
        {
            // Use Unix timestamp so the URL is unique every session.
            // This prevents the in-game CEF browser from serving any previously
            // cached version (e.g. ?v=19 cached before Cache-Control: no-store
            // was added) — any cached entry uses a different URL and is ignored.
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return $"<script src=\"/automation.js?t={ts}\"></script>";
        }
    }
}
