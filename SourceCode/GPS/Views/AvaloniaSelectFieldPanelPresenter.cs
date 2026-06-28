// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Core.ViewModels;
using AgOpenGPS.Views.Field;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    // [XPLAT] ISelectFieldPanelPresenter impl; opens Views/Field MVVM dialogs; confirm-delete via FormDialogView.ShowQuestionBlocking.
    /// <summary>
    /// Avalonia implementation of <see cref="ISelectFieldPanelPresenter"/>. It owns the lifetime of the
    /// four Select-Field-flow dialog windows — <see cref="SelectFieldMenuView"/> (the field menu),
    /// <see cref="SelectNearFieldView"/> (nearest-field picker), <see cref="SelectFieldView"/> (the field
    /// picker / opener) and <see cref="CreateFromExistingFieldView"/> (create-from-existing) — that live
    /// in the sibling <c>Views/Field/</c> folder (namespace <see cref="AgOpenGPS.Views.Field"/>) and binds
    /// each to its matching Core view-model (<see cref="SelectFieldMenuViewModel"/>,
    /// <see cref="SelectNearFieldViewModel"/>, <see cref="SelectFieldViewModel"/> and
    /// <see cref="CreateFromExistingFieldViewModel"/>). It also surfaces the synchronous confirm-delete
    /// message box used when the user removes a field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This class replaces the Windows Forms field-selection navigation that
    /// <c>FormFieldMenu</c> / <c>FormFieldOpen</c> / <c>FormFieldExisting</c> drove with
    /// <c>using (var form = new FormFieldOpen(this)) { form.ShowDialog(this); }</c>. Because those original
    /// presentations were <em>modal</em>, the Avalonia replacements are shown modally too —
    /// <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/> parented on
    /// <see cref="Owner"/> — preserving the WinForms behaviour where the main window is blocked while a
    /// field dialog is open. The Core view-models drive the navigation: the menu's commands call
    /// <see cref="CloseSelectFieldMenuDialog"/> and then one of the
    /// <c>Show…Dialog</c> methods, so the menu closes as the chosen sub-dialog opens (mirroring the
    /// already-proven <c>AvaloniaConfigMenuPanelPresenter</c> flow).
    /// </para>
    /// <para>
    /// <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/> is awaitable (it returns
    /// a <see cref="System.Threading.Tasks.Task"/>), but the <see cref="ISelectFieldPanelPresenter"/>
    /// contract for the show/close members is synchronous (<see langword="void"/>). The modal task is
    /// therefore intentionally fire-and-forget — the same pattern already used by the migrated
    /// <c>AvaloniaConfigMenuPanelPresenter</c> — and completes naturally when the dialog is closed (either
    /// by the view-model navigation or by the user). When no <see cref="Owner"/> has been assigned yet (for
    /// example before <c>MainView</c> exists, or in a headless test host) the dialog is shown modelessly
    /// with <see cref="Avalonia.Controls.Window.Show()"/>, because Avalonia's <c>ShowDialog</c> requires a
    /// non-<see langword="null"/>, visible owner.
    /// </para>
    /// <para>
    /// All show/close work is marshalled onto the Avalonia UI thread via
    /// <see cref="Dispatcher.UIThread"/> so the presenter is safe to call from non-UI callers (the
    /// extracted services / scan loop). Every open dialog reference is cleared on the window's
    /// <see cref="Avalonia.Controls.Window.Closed"/> event, so a dialog the user closes directly does not
    /// leave a stale reference behind, and re-opening is always double-open safe.
    /// </para>
    /// <para>
    /// <see cref="ShowConfirmDeleteMessageBox"/> is the one member that must return a value
    /// <em>synchronously</em> (a plain <see cref="bool"/>, because the Core
    /// <see cref="SelectFieldViewModel"/> consumes it inline inside its delete command). It delegates to
    /// the foundational <see cref="FormDialogView.ShowQuestionBlocking"/> helper, which reproduces the
    /// WinForms <c>FormDialog.ShowQuestion</c> blocking semantics over Avalonia's async modal model with a
    /// nested dispatcher loop. That helper must be invoked on the UI thread (it enforces this via
    /// <see cref="Dispatcher.VerifyAccess"/>); the delete command already runs on the UI thread, so the
    /// call is made directly rather than being marshalled (marshalling would forfeit the synchronous
    /// return).
    /// </para>
    /// </remarks>
    public class AvaloniaSelectFieldPanelPresenter : ISelectFieldPanelPresenter
    {
        /// <summary>
        /// The currently open field-menu dialog, or <see langword="null"/> when none is open.
        /// </summary>
        private SelectFieldMenuView _selectFieldMenuView;

        /// <summary>
        /// The currently open nearest-field dialog, or <see langword="null"/> when none is open.
        /// </summary>
        private SelectNearFieldView _selectNearFieldView;

        /// <summary>
        /// The currently open field-picker dialog, or <see langword="null"/> when none is open.
        /// </summary>
        private SelectFieldView _selectFieldView;

        /// <summary>
        /// The currently open create-from-existing-field dialog, or <see langword="null"/> when none is
        /// open.
        /// </summary>
        private CreateFromExistingFieldView _createFromExistingFieldView;

        /// <summary>
        /// Gets or sets the window that parents (owns) the Select-Field-flow dialogs and the confirm-delete
        /// message box. It is assigned by <c>App.axaml.cs</c> once the main view exists. When set, dialogs
        /// are shown modally with <see cref="Avalonia.Controls.Window.ShowDialog(Avalonia.Controls.Window)"/>;
        /// when <see langword="null"/>, dialogs fall back to a modeless
        /// <see cref="Avalonia.Controls.Window.Show()"/>.
        /// </summary>
        public Window Owner { get; set; }

        /// <summary>
        /// Opens the field-menu dialog bound to <paramref name="viewModel"/>. Any menu dialog already open
        /// is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the field menu.</param>
        public void ShowSelectFieldMenuDialog(SelectFieldMenuViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing menu instance first.
                CloseSelectFieldMenuViewCore();

                _selectFieldMenuView = new SelectFieldMenuView { DataContext = viewModel };
                _selectFieldMenuView.Closed += OnSelectFieldMenuViewClosed;
                ShowOwned(_selectFieldMenuView);
            });
        }

        /// <summary>
        /// Closes the field-menu dialog if it is open and clears its reference. Safe to call when no menu
        /// dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseSelectFieldMenuDialog()
        {
            RunOnUiThread(CloseSelectFieldMenuViewCore);
        }

        /// <summary>
        /// Opens the nearest-field dialog bound to <paramref name="viewModel"/>. Any nearest-field dialog
        /// already open is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the nearest-field picker.</param>
        public void ShowSelectNearFieldDialog(SelectNearFieldViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing instance first.
                CloseSelectNearFieldViewCore();

                _selectNearFieldView = new SelectNearFieldView { DataContext = viewModel };
                _selectNearFieldView.Closed += OnSelectNearFieldViewClosed;
                ShowOwned(_selectNearFieldView);
            });
        }

        /// <summary>
        /// Closes the nearest-field dialog if it is open and clears its reference. Safe to call when no
        /// nearest-field dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseSelectNearFieldDialog()
        {
            RunOnUiThread(CloseSelectNearFieldViewCore);
        }

        /// <summary>
        /// Opens the field-picker dialog bound to <paramref name="viewModel"/>. Any field-picker dialog
        /// already open is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the field picker.</param>
        public void ShowSelectFieldDialog(SelectFieldViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing instance first.
                CloseSelectFieldViewCore();

                _selectFieldView = new SelectFieldView { DataContext = viewModel };
                _selectFieldView.Closed += OnSelectFieldViewClosed;
                ShowOwned(_selectFieldView);
            });
        }

        /// <summary>
        /// Closes the field-picker dialog if it is open and clears its reference. Safe to call when no
        /// field-picker dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseSelectFieldDialog()
        {
            RunOnUiThread(CloseSelectFieldViewCore);
        }

        /// <summary>
        /// Opens the create-from-existing-field dialog bound to <paramref name="viewModel"/>. Any such
        /// dialog already open is closed first (double-open safe). Marshalled onto the UI thread.
        /// </summary>
        /// <param name="viewModel">The Core view-model that backs the create-from-existing flow.</param>
        public void ShowCreateFromExistingFieldDialog(CreateFromExistingFieldViewModel viewModel)
        {
            RunOnUiThread(() =>
            {
                // [XPLAT] guard against a double-open: close/replace any existing instance first.
                CloseCreateFromExistingFieldViewCore();

                _createFromExistingFieldView = new CreateFromExistingFieldView { DataContext = viewModel };
                _createFromExistingFieldView.Closed += OnCreateFromExistingFieldViewClosed;
                ShowOwned(_createFromExistingFieldView);
            });
        }

        /// <summary>
        /// Closes the create-from-existing-field dialog if it is open and clears its reference. Safe to
        /// call when no such dialog is open. Marshalled onto the UI thread.
        /// </summary>
        public void CloseCreateFromExistingFieldDialog()
        {
            RunOnUiThread(CloseCreateFromExistingFieldViewCore);
        }

        /// <summary>
        /// [XPLAT] Shows the modal "delete this field?" confirmation and returns the user's answer
        /// <em>synchronously</em>, mirroring the WinForms <c>FormYes</c> / <c>FormDialog.ShowQuestion</c>
        /// confirm pattern. Returns <see langword="true"/> only on an affirmative (OK) response.
        /// </summary>
        /// <remarks>
        /// The <see cref="ISelectFieldPanelPresenter"/> contract makes this member synchronous because the
        /// Core <see cref="SelectFieldViewModel"/> consumes the result inline inside its delete command, so
        /// it cannot be made <c>async</c>. The work is therefore delegated to
        /// <see cref="FormDialogView.ShowQuestionBlocking"/>, which reproduces the WinForms blocking
        /// <c>ShowDialog()</c> semantics on Avalonia's async modal model via a nested dispatcher loop. That
        /// helper must run on the UI thread and enforces it (<see cref="Dispatcher.VerifyAccess"/>); the
        /// delete command already executes on the UI thread, so the helper is invoked directly — wrapping
        /// it in a UI-thread <c>Post</c> would forfeit the synchronous return the contract requires.
        /// </remarks>
        /// <param name="fieldName">The name of the field the user is about to delete.</param>
        /// <returns><see langword="true"/> if the user confirmed the deletion; otherwise
        /// <see langword="false"/>.</returns>
        public bool ShowConfirmDeleteMessageBox(string fieldName)
        {
            // [XPLAT] Localised title ("Delete Field") + a confirmation question that names the field.
            // gStr.gsDeleteForSure == "Are you sure you want to delete?"; the field name is shown beneath
            // it so the message conveys deletion of the named field. Coalesce a null name to an empty
            // string so "null" is never rendered as visible text.
            string title = gStr.gsDeleteField;
            string message = $"{gStr.gsDeleteForSure}{Environment.NewLine}{Environment.NewLine}{fieldName ?? string.Empty}";

            // Synchronous bool via the foundational blocking helper. Warning severity (amber frame) matches
            // the destructive nature of the action; Owner parents the box (the helper falls back to the
            // app-wide FormDialogView.Owner when this is null). Returns true only on the affirmative.
            return FormDialogView.ShowQuestionBlocking(title, message, DialogSeverity.Warning, Owner);
        }

        /// <summary>
        /// [XPLAT] Presents <paramref name="window"/> modally on <see cref="Owner"/> when an owner is
        /// available (mirroring the original modal <c>FormFieldOpen.ShowDialog(this)</c>), otherwise shows
        /// it modelessly. The modal task is fire-and-forget because the show/close interface members are
        /// synchronous; it completes when the dialog closes.
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
        /// Closes and clears the field-menu dialog. The field is cleared <em>before</em> the window is
        /// closed and its handler detached, so the <see cref="Avalonia.Controls.Window.Closed"/> callback
        /// cannot re-enter or clobber a replacement opened immediately afterwards.
        /// </summary>
        private void CloseSelectFieldMenuViewCore()
        {
            SelectFieldMenuView view = _selectFieldMenuView;
            _selectFieldMenuView = null;
            if (view != null)
            {
                view.Closed -= OnSelectFieldMenuViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Closes and clears the nearest-field dialog. See <see cref="CloseSelectFieldMenuViewCore"/> for
        /// the ordering rationale.
        /// </summary>
        private void CloseSelectNearFieldViewCore()
        {
            SelectNearFieldView view = _selectNearFieldView;
            _selectNearFieldView = null;
            if (view != null)
            {
                view.Closed -= OnSelectNearFieldViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Closes and clears the field-picker dialog. See <see cref="CloseSelectFieldMenuViewCore"/> for
        /// the ordering rationale.
        /// </summary>
        private void CloseSelectFieldViewCore()
        {
            SelectFieldView view = _selectFieldView;
            _selectFieldView = null;
            if (view != null)
            {
                view.Closed -= OnSelectFieldViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Closes and clears the create-from-existing-field dialog. See
        /// <see cref="CloseSelectFieldMenuViewCore"/> for the ordering rationale.
        /// </summary>
        private void CloseCreateFromExistingFieldViewCore()
        {
            CreateFromExistingFieldView view = _createFromExistingFieldView;
            _createFromExistingFieldView = null;
            if (view != null)
            {
                view.Closed -= OnCreateFromExistingFieldViewClosed;
                view.Close();
            }
        }

        /// <summary>
        /// Clears the field-menu reference when the user (or any other path) closes the window directly,
        /// preventing a stale reference. Only clears when the field still points at the window that raised
        /// the event, so a freshly opened replacement is never cleared by mistake.
        /// </summary>
        private void OnSelectFieldMenuViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnSelectFieldMenuViewClosed;
            }

            if (ReferenceEquals(_selectFieldMenuView, sender))
            {
                _selectFieldMenuView = null;
            }
        }

        /// <summary>
        /// Clears the nearest-field reference when the window is closed directly. See
        /// <see cref="OnSelectFieldMenuViewClosed"/> for the matching rationale.
        /// </summary>
        private void OnSelectNearFieldViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnSelectNearFieldViewClosed;
            }

            if (ReferenceEquals(_selectNearFieldView, sender))
            {
                _selectNearFieldView = null;
            }
        }

        /// <summary>
        /// Clears the field-picker reference when the window is closed directly. See
        /// <see cref="OnSelectFieldMenuViewClosed"/> for the matching rationale.
        /// </summary>
        private void OnSelectFieldViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnSelectFieldViewClosed;
            }

            if (ReferenceEquals(_selectFieldView, sender))
            {
                _selectFieldView = null;
            }
        }

        /// <summary>
        /// Clears the create-from-existing reference when the window is closed directly. See
        /// <see cref="OnSelectFieldMenuViewClosed"/> for the matching rationale.
        /// </summary>
        private void OnCreateFromExistingFieldViewClosed(object sender, EventArgs e)
        {
            if (sender is Window window)
            {
                window.Closed -= OnCreateFromExistingFieldViewClosed;
            }

            if (ReferenceEquals(_createFromExistingFieldView, sender))
            {
                _createFromExistingFieldView = null;
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
