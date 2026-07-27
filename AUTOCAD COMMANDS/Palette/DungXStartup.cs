using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

[assembly: ExtensionApplication(typeof(AUTOCAD_COMMANDS.DungXStartup))]

namespace AUTOCAD_COMMANDS
{
    public sealed class DungXStartup : IExtensionApplication
    {
        public void Initialize()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            WorkspaceUiStateStore.Initialize(ed);
            WorkspaceUiStateStore.SaveValues(
                new Dictionary<string, string>
                {
                    ["startup.loadedDll"] = Assembly.GetExecutingAssembly().Location,
                    ["startup.lastInitializeUtc"] = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                });

            CalculatorCommands.InitializeApplication();
            DungXPaletteHost.Initialize();
            DungXRibbonHost.Initialize();
        }

        public void Terminate()
        {
            System.Exception firstError = null;

            TryTerminate(() => DungXRibbonHost.Terminate(), ref firstError);
            TryTerminate(() => DungXPaletteHost.Terminate(), ref firstError);
            TryTerminate(() => CalculatorCommands.TerminateApplication(), ref firstError);
            TryTerminate(
                () => WorkspaceUiStateStore.SaveValue(
                    "startup.lastTerminateUtc",
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                ref firstError);

            if (firstError != null)
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n[DUNGX] Startup terminate warning: " + firstError.Message);
            }
        }

        private static void TryTerminate(Action action, ref System.Exception firstError)
        {
            try
            {
                action();
            }
            catch (System.Exception ex)
            {
                if (firstError == null)
                {
                    firstError = ex;
                }
            }
        }
    }
}
