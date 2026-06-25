// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.ViewModels;
using AgOpenGPS.Views.Config;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    // [XPLAT] IConfigMenuPanelPresenter impl; opens Views/Config MVVM dialogs (ConfigMenuView/ConfigView) bound to Core VMs; replaces FormConfig modal navigation.
    /// <summary>
    /// Avalonia implementation of <see cref="IConfigMenuPanelPresenter"/>. It owns the lifetime of the
    /// two Config-flow dialog windows — <see cref="ConfigMenuView"/> (the configuration menu) and
    /// <see cref="ConfigView"/> (the configuration dialog) — that live in the sibling
    /// <c>Views/Config/</c> folder (namespace <see cref="AgOpenGPS.Views.Config"/>) and binds each to the
    /// matching Core view-model (<see cref="ConfigMenuViewModel"/> / <see cref="ConfigViewModel"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This class replaces the Windows Forms <c>FormConfig</c> navigation that the original
    /// <c>Controls.Designer.cs btnConfig_Click</c> drove with
    /// <c>using (FormConfig form = new FormConfig(this)) { form.ShowDialog(this); }</c>. Because that
    /// original presentation was <em>modal</em>, the Avalonia replacements are shown modally too —
    /// <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/> parented on
    /// <see cref="Owner"/> — preserving the WinForms behaviour where the main window is blocked while a
    /// configuration dialog is open. The Core view-models drive the navigation: the menu's
    /// <c>ShowConfigurationDialogCommand</c> calls <see cref="CloseConfigMenuDialog"/> and then
    /// <see cref="ShowConfigDialog(ConfigViewModel)"/>, so the menu closes as the configuration dialog
    /// opens.
    /// </para>
    /// <para>
    /// <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/> is awaitable (it
    /// returns a <see cref="System.Threading.Tasks.Task"/>), but the <see cref="IConfigMenuPanelPresenter"/>
    /// contract is synchronous (<see langword="void"/>). The modal task is therefore intentionally
    /// fire-and-forget — the same pattern already used elsewhere in the migrated codebase
    /// (e.g. <c>GPS_Out</c>'s <c>clsTools.ShowHelp</c>) — and completes naturally when the dialog is
    /// closed (either by the view-model navigation or by the user). When no <see cref="Owner"/> has been
    /// assigned yet (for example before <c>MainView</c> exists, or in a headless test host) the dialog is
    /// shown modelessly with <see cref="Avalonia.Controls.Window.Show()"/>, because Avalonia's
    /// <c>ShowDialog</c> requires a non-<see langword="null"/>, visible owner.
    /// </para>
    /// <para>
    /// All show/close work is marshalled onto the Avalonia UI thread via
    /// <see cref="Dispatcher.UIThread"/> so the presenter is safe to call from non-UI callers (the
    /// extracted services / scan loop). Every open dialog reference is cleared on the window's
    /// <see cref="Avalonia.Controls.Window.Closed"/> event, so a dialog the user closes directly does not
    /// leave a stale reference behind, and re-opening is always double-open safe.
    /// </para>
    /// </remarks>
    public class AvaloniaConfigMenuPanelPresenter : IConfigMenuPanelPresenter
    {
        /// <summary>
        /// The currently open configuration-menu dialog, or <see langword="null"/> when none is open.
        /// </summary>
        private ConfigMenuView _configMenuView;

        /// <summary>
        /// The currently open configuration dialog, or <see langword="null"/> when none is open.
        /// </summary>
        private ConfigView _configView;

        /// <summary>
        /// Gets or sets the window that parents (owns) the Config-flow dialogs. It is assigned by
        /// <c>App.axaml.cs</c> once the main view exists. When set, dialogs are shown modally with
        /// <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/>; when
        /// <see langword="null"/>, dialogs fall back to a modeless
        /// <see cref="Avalonia.Controls.Window.Show()"/>.
        /// </summary>
        public Window Owner { get; set; }

        /// <summary>
        /// Opens the configuration-menu dialog bound to <paramref name="viewModel"/>. Any menu dialog
        /// already open is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the configuration menu.</param>
        public void ShowConfigMenuDialog(ConfigMenuViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing menu instance first.
                CloseConfigMenuViewCore();

                _configMenuView = new ConfigMenuView { DataContext = viewModel };
                _configMenuView.Closed += OnConfigMenuViewClosed;
                ShowOwned(_configMenuView);
            });
        }

        /// <summary>
        /// Closes the configuration-menu dialog if it is open and clears its reference. Safe to call when
        /// no menu dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseConfigMenuDialog()
        {
            RunOnUiThread(CloseConfigMenuViewCore);
        }

        /// <summary>
        /// Opens the configuration dialog bound to <paramref name="viewModel"/>. Any configuration dialog
        /// already open is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the configuration dialog.</param>
        public void ShowConfigDialog(ConfigViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing config instance first.
                CloseConfigViewCore();

                _configView = new ConfigView { DataContext = viewModel };
                _configView.Closed += OnConfigViewClosed;
                ShowOwned(_configView);
            });
        }

        /// <summary>
        /// Closes the configuration dialog if it is open and clears its reference. Safe to call when no
        /// configuration dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseConfigDialog()
        {
            RunOnUiThread(CloseConfigViewCore);
        }

        /// <summary>
        /// [XPLAT] Presents <paramref name="window"/> modally on <see cref="Owner"/> when an owner is
        /// available (mirroring the original modal <c>FormConfig.ShowDialog(this)</c>), otherwise shows it
        /// modelessly. The modal task is fire-and-forget because the interface is synchronous; it
        /// completes when the dialog closes.
        /// </summary>
        /// <param name="window">The dialog window to present.</param>
        private void ShowOwned(Window window)
        {
            if (Owner != null)
            {
                // Fire-and-forget: ShowDialog returns a Task that completes on close; the void contract
                // cannot await it. Discarded explicitly to document the intentional non-await.
                _ = window.ShowDialog(Owner);
            }
            else
            {
                window.Show();
            }
        }

        /// <summary>
        /// Closes and clears the configuration-menu dialog. The field is cleared <em>before</em> the
        /// window is closed and its handler detached, so the <see cref="Avalonia.Controls.Window.Closed"/>
        /// callback cannot re-enter or clobber a replacement opened immediately afterwards.
        /// </summary>
        private void CloseConfigMenuViewCore()
        {
            ConfigMenuView view = _configMenuView;
            _configMenuView = null;
            if (view != null)
            {
                view.Closed -= OnConfigMenuViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Closes and clears the configuration dialog. See <see cref="CloseConfigMenuViewCore"/> for the
        /// ordering rationale.
        /// </summary>
        private void CloseConfigViewCore()
        {
            ConfigView view = _configView;
            _configView = null;
            if (view != null)
            {
                view.Closed -= OnConfigViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Clears the menu-dialog reference when the user (or any other path) closes the window directly,
        /// preventing a stale reference. Only clears when the field still points at the window that
        /// raised the event, so a freshly opened replacement is never cleared by mistake.
        /// </summary>
        private void OnConfigMenuViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnConfigMenuViewClosed;
            }

            if (ReferenceEquals(_configMenuView, sender))
            {
                _configMenuView = null;
            }
        }

        /// <summary>
        /// Clears the configuration-dialog reference when the window is closed directly. See
        /// <see cref="OnConfigMenuViewClosed"/> for the matching rationale.
        /// </summary>
        private void OnConfigViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnConfigViewClosed;
            }

            if (ReferenceEquals(_configView, sender))
            {
                _configView = null;
            }
        }

        /// <summary>
        /// [XPLAT] Runs <paramref name="action"/> on the Avalonia UI thread, executing it inline when the
        /// caller is already on the UI thread and posting it otherwise. This mirrors the WinForms
        /// <c>InvokeRequired</c>/<c>BeginInvoke</c> marshalling and lets non-UI callers (the extracted
        /// services) drive the presenter safely.
        /// </summary>
        /// <param name="action">The show/close work to execute on the UI thread.</param>
        private static void RunOnUiThread(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
    }
}
