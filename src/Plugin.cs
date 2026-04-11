using System;
using System.IO;
using System.Reflection;
using Timberborn.ModManagerScene;

namespace HTTPAutomation
{
    public class Plugin : IModStarter
    {
        public static string ModDirectory { get; private set; } = "";

        public void StartMod(IModEnvironment modEnvironment)
        {
            // IModEnvironment only exposes ModPath and OriginPath (confirmed from probe).
            // IConfigurator is auto-discovered by Bindito.InstallAll("Game") —
            // just set ModDirectory here, no manual registration needed.

            var val = modEnvironment.ModPath;
            if (!string.IsNullOrEmpty(val) && Directory.Exists(val))
            { ModDirectory = val; return; }

            // Fallback: scan for our marker file
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Timberborn", "Mods");
            if (Directory.Exists(root))
                foreach (var dir in Directory.GetDirectories(root))
                    if (File.Exists(Path.Combine(dir, "HttpApi", "index.hbs")))
                    { ModDirectory = dir; return; }

            // DLL parent fallback
            var dllDir = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
            var parent = Path.GetDirectoryName(dllDir) ?? "";
            if (File.Exists(Path.Combine(parent, "HttpApi", "index.hbs")))
                ModDirectory = parent;
        }
    }
}
