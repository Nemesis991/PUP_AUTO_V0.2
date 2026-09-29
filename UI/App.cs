using System;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;

// Registers this class as the extension application entry point
[assembly: ExtensionApplication(typeof(PUP_AUTO.UI.App))]

namespace PUP_AUTO.UI
{
    /// <summary>
    /// AutoCAD extension entry point. Called automatically when
    /// the plugin is loaded (via NETLOAD or .bundle auto-load).
    /// Creates a Ribbon Tab for PUP_AUTO.
    /// </summary>
    public class App : IExtensionApplication
    {
        public void Initialize()
        {
            // Ribbon initialization must be deferred until the AutoCAD
            // application is fully loaded and the Ribbon is available.
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnAppIdle;
        }

        public void Terminate()
        {
            // Cleanup if needed
        }

        /// <summary>
        /// Fires once on the first Application.Idle event after loading.
        /// Creates the PUP_AUTO Ribbon Tab with an "Отвори Прозорец" button.
        /// </summary>
        private void OnAppIdle(object sender, EventArgs e)
        {
            // Unsubscribe immediately — we only need this once
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnAppIdle;

            try
            {
                CreateRibbon();
            }
            catch (System.Exception ex)
            {
                // Log to command line but don't crash AutoCAD
                Document doc = Application.DocumentManager.MdiActiveDocument;
                doc?.Editor?.WriteMessage(
                    $"\n[PUP_AUTO] Warning: Could not create Ribbon tab: {ex.Message}\n");
            }
        }

        private void CreateRibbon()
        {
            var ribbonCtrl = Autodesk.Windows.ComponentManager.Ribbon;
            if (ribbonCtrl == null) return;

            // Check if tab already exists (e.g. from a previous NETLOAD)
            const string tabId = "PUP_AUTO_TAB";
            foreach (var existingTab in ribbonCtrl.Tabs)
            {
                if (existingTab.Id == tabId) return;
            }

            // ── Create Tab ──
            var tab = new Autodesk.Windows.RibbonTab
            {
                Title = "ПУП АВТОМАТИЗАЦИЯ",
                Id = tabId
            };

            // ── Create Panel ──
            var panelSrc = new Autodesk.Windows.RibbonPanelSource
            {
                Title = "Генериране"
            };
            var panel = new Autodesk.Windows.RibbonPanel { Source = panelSrc };

            // ── Open GUI Button ──
            var btnGui = new Autodesk.Windows.RibbonButton
            {
                Text = "Отвори\nПрозорец",
                ShowText = true,
                Size = Autodesk.Windows.RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandParameter = "PUP_WINDOW ",
                CommandHandler = new RibbonCommandHandler()
            };

            panelSrc.Items.Add(btnGui);

            tab.Panels.Add(panel);
            ribbonCtrl.Tabs.Add(tab);
        }
    }

    /// <summary>
    /// Routes Ribbon button clicks to AutoCAD commands.
    /// </summary>
    internal class RibbonCommandHandler : System.Windows.Input.ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter)
        {
            if (parameter is string cmd && !string.IsNullOrWhiteSpace(cmd))
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                doc?.SendStringToExecute(cmd, true, false, false);
            }
        }
    }
}
