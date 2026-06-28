// [XPLAT] migrated from net48/WinForms FormProfiles.cs + FormProfiles.designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using AgIO.Controls;
using AgOpenGPS.Core.Interfaces;

namespace AgIO.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormProfiles.axaml</c> — the AgIO "Manage Profiles" dialog,
    /// reimplemented as an Avalonia <see cref="Window"/> that replaces the deleted WinForms
    /// <c>FormProfiles : Form</c> (<c>Forms/FormProfiles.cs</c> + <c>Forms/FormProfiles.designer.cs</c>,
    /// namespace <c>AgIO</c>, constructor <c>FormProfiles(Form callingForm)</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Window-bound concerns only.</b> Every behaviour — profile enumeration, load/save/delete, name
    /// sanitisation, and the apply/cancel flow — lives in the bound <see cref="FormProfilesViewModel"/>
    /// (set as the <see cref="StyledElement.DataContext"/>), and the paired <c>FormProfiles.axaml</c>
    /// drives every affordance through compiled command bindings
    /// (<c>x:DataType="vm:FormProfilesViewModel"</c>). A view-model, however, can neither own an Avalonia
    /// <see cref="Window"/> nor pop a modal dialog, so this code-behind fulfils the three concerns that
    /// are intrinsically window-bound — exactly the responsibilities the <c>FormProfiles.axaml</c>
    /// header documents for it:
    /// <list type="number">
    ///   <item><description><b>Confirmations.</b> The WinForms overwrite/delete
    ///   <c>MessageBox.Show(..., MessageBoxButtons.YesNo, ...)</c> prompts are surfaced through the
    ///   view-model's awaitable <see cref="FormProfilesViewModel.ConfirmAsync"/> callback, which this
    ///   class wires to <c>new FormYes(message, true).ShowDialog&lt;bool&gt;(this)</c>.</description></item>
    ///   <item><description><b>Dialog dismissal.</b> The view-model raises
    ///   <see cref="FormProfilesViewModel.RequestClose"/> (the former <c>Close()</c>); this class routes
    ///   it to <see cref="Window.Close()"/> so the host's <c>ShowDialog</c> resolves.</description></item>
    ///   <item><description><b>On-screen keyboard.</b> The WinForms textbox-tap handler
    ///   (<c>((TextBox)sender).ShowKeyboard(this)</c>, gated on <c>FormLoop.isKeyboardOn</c>) is
    ///   reproduced here by attaching the converted <c>../Controls/</c>
    ///   <see cref="TextBoxExtensions.ShowKeyboard"/> helper to the named <c>tboxName</c> entry.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>God-object removal (AAP §0.3.2).</b> The WinForms dialog took a <c>FormLoop</c> back-reference
    /// (<c>mf</c>) used only to read the touch-keyboard flag. That coupling is gone: this dialog is
    /// constructed from an injected <see cref="IErrorPresenter"/> alone (no <c>Form callingForm</c>),
    /// builds and owns its own <see cref="FormProfilesViewModel"/>, and surfaces the keyboard flag as a
    /// local view concern. AgIO hard-coded <c>isKeyboardOn = true</c>, so the on-screen keyboard is
    /// offered by default; the explicit <see cref="_isKeyboardOn"/> field documents and preserves that
    /// behaviour. The host is expected to open this dialog as
    /// <c>await new FormProfiles(errorPresenter).ShowDialog(owner)</c> (parity with the WinForms
    /// <c>FormLoop</c> profiles command), subscribing <see cref="FormProfilesViewModel.ProfileApplied"/>
    /// itself if it needs to reload settings-derived state; this code-behind deliberately leaves that to
    /// the host.
    /// </para>
    /// <para>
    /// <b>Conventions.</b> A parameterless constructor for the Avalonia XAML loader plus a
    /// dependency-supplying overload, an explicit <see cref="AvaloniaXamlLoader"/>-based
    /// <see cref="InitializeComponent"/>, and <see cref="NameScopeExtensions.FindControl{T}"/> for the
    /// <c>x:Name</c>d controls — mirroring the sibling AgIO views (<c>FormSource</c>, <c>FormYes</c>,
    /// <c>FormKeyboard</c>, <c>FormRadioChannel</c>). Nullable reference types are disabled project-wide,
    /// so this file uses no nullable annotations and guards reference values with explicit
    /// <see langword="null"/> checks. No WinForms/WPF/System.Drawing types are referenced.
    /// </para>
    /// </remarks>
    public partial class FormProfiles : Window
    {
        // [XPLAT] Title shown on the keyboard-failure notice; mirrors the window Title for consistency.
        private const string DialogTitle = "AgIO: Manage Profiles";

        // [XPLAT] Whether the on-screen touch keyboard is offered when the name field gains focus. The
        // WinForms dialog gated this on FormLoop.isKeyboardOn, which AgIO hard-coded to true; this field
        // preserves that default. It is an instance field (not a const) so the gate stays a runtime
        // decision — exactly as it was in the WinForms original — and could later be sourced from
        // settings without changing this contract.
        private readonly bool _isKeyboardOn = true;

        // [XPLAT] The view-model backing this dialog. Built in FormProfiles(IErrorPresenter) and retained
        // for the dialog's lifetime (it is both this field and the window's DataContext). The
        // parameterless loader constructor leaves it at its default — that path never touches it.
        private readonly FormProfilesViewModel _vm;

        // [XPLAT] Informational sink (the cross-platform replacement for MessageBox.Show(...OK...)).
        // Held so the on-screen-keyboard handler can report a failure rather than crash the async-void
        // boundary; null is tolerated (every use is null-checked) for graceful degradation.
        private readonly IErrorPresenter _errorPresenter;

        // [XPLAT] Re-entrancy guard for the keyboard handler. Opening the modal keyboard moves focus away
        // and closing it restores focus to the name field, which would re-raise GotFocus; this flag (plus
        // moving focus to Save afterwards) prevents the keyboard from reopening in a loop.
        private bool _keyboardShowing;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader and the design-time
        /// previewer, and to keep the compiled-XAML resource reachable (otherwise the build emits
        /// warning <c>AVLN3001</c>, which the Release configuration — with <c>TreatWarningsAsErrors</c> —
        /// promotes to an error). Application code constructs the dialog through
        /// <see cref="FormProfiles(IErrorPresenter)"/>.
        /// </summary>
        public FormProfiles()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Creates the dialog, builds and binds its <see cref="FormProfilesViewModel"/> from the injected
        /// <paramref name="errorPresenter"/>, and wires the three window-bound concerns the view-model
        /// delegates to the view: the Yes/No confirmation callback (via <see cref="FormYes"/>), the
        /// close-request routing, and the on-screen keyboard. This is the cross-platform replacement for
        /// the WinForms <c>FormProfiles(Form callingForm)</c> constructor, with the <c>Form callingForm</c>
        /// god-object parameter dropped in favour of the single injected presenter.
        /// </summary>
        /// <param name="errorPresenter">
        /// The sink used for informational (OK-only) notifications — the cross-platform replacement for
        /// the WinForms <c>MessageBox.Show(...OK...)</c> calls. Forwarded to the view-model and retained
        /// for the keyboard handler's failure reporting; a <see langword="null"/> value is tolerated.
        /// </param>
        public FormProfiles(IErrorPresenter errorPresenter)
        {
            InitializeComponent();

            _errorPresenter = errorPresenter;

            // [XPLAT] The window owns its view-model; both share this dialog's lifetime. The VM has no
            // parameterless constructor (it requires the presenter), so the DataContext is assigned here
            // rather than in the .axaml.
            _vm = new FormProfilesViewModel(errorPresenter);
            DataContext = _vm;

            // [XPLAT] Yes/No confirmation callback (overwrite + delete). Replaces the WinForms
            // MessageBox.Show(..., YesNo, ...) prompts: the VM awaits this and proceeds only on true. The
            // modal FormYes dialog returns its result through ShowDialog<bool> (true = OK/Yes).
            _vm.ConfirmAsync = async message => await new FormYes(message, true).ShowDialog<bool>(this);

            // [XPLAT] The VM raises RequestClose (the former Close()) on cancel and after a profile is
            // applied/saved; route it to the window close so the host's ShowDialog resolves. The window
            // owns the VM and closes when this fires, so the lambda needs no explicit unsubscribe.
            _vm.RequestClose += () => Close();

            // [XPLAT] On-screen keyboard wiring (WinForms tbox Click -> ShowKeyboard). Resolve the named
            // entry through the name scope (the explicit XAML loader does not generate typed fields) and
            // open the keyboard when the field gains focus. ProfileApplied is intentionally left for the
            // host (MainWindow) to subscribe, per the dialog contract.
            TextBox nameTextBox = this.FindControl<TextBox>("tboxName");
            if (nameTextBox != null)
            {
                nameTextBox.GotFocus += OnNameGotFocus;
            }
        }

        /// <summary>
        /// [XPLAT] Handles focus entering the new-profile-name field — the cross-platform, touch-friendly
        /// analogue of the WinForms <c>tboxCreateNew</c>/<c>tboxSaveAs</c> <c>Click</c> handlers that
        /// opened the on-screen keyboard. This is the thin <c>async void</c> event boundary; the awaitable
        /// work is delegated to <see cref="ShowKeyboardForAsync(TextBox)"/> so exceptions never escape the
        /// void-async edge.
        /// </summary>
        /// <param name="sender">The name <see cref="TextBox"/> that gained focus.</param>
        /// <param name="e">The focus-gained event data (unused).</param>
        private async void OnNameGotFocus(object sender, GotFocusEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null)
            {
                return;
            }

            await ShowKeyboardForAsync(textBox);
        }

        /// <summary>
        /// [XPLAT] Opens the on-screen <see cref="FormKeyboard"/> for the supplied <paramref name="textBox"/>
        /// when the touch keyboard is enabled, then moves focus to the Save button — parity with the
        /// WinForms <c>((TextBox)sender).ShowKeyboard(this); btnSaveNewProfile.Focus();</c> sequence.
        /// </summary>
        /// <param name="textBox">The name entry whose text the keyboard edits.</param>
        /// <returns>A task that completes when the keyboard dialog has closed and focus has been moved.</returns>
        /// <remarks>
        /// The converted <see cref="TextBoxExtensions.ShowKeyboard"/> helper writes the typed result back
        /// into <see cref="TextBox.Text"/>, which the two-way binding propagates to
        /// <see cref="FormProfilesViewModel.NewProfileName"/> — so no value is plumbed manually. The
        /// <see cref="_keyboardShowing"/> guard plus the post-close focus move prevent the keyboard from
        /// reopening when focus returns to the field after the modal closes, and any failure is reported
        /// through the injected presenter (when present) instead of faulting the calling
        /// <c>async void</c> handler.
        /// </remarks>
        private async Task ShowKeyboardForAsync(TextBox textBox)
        {
            if (!_isKeyboardOn || _keyboardShowing)
            {
                return;
            }

            _keyboardShowing = true;
            try
            {
                // [XPLAT] Modal on-screen keyboard; writes the result back into textBox.Text (two-way
                // bound to NewProfileName). Parity with WinForms ((TextBox)sender).ShowKeyboard(this).
                await textBox.ShowKeyboard(this);

                // [XPLAT] Parity with WinForms btnSaveNewProfile.Focus(): move focus off the field so a
                // deliberate new tap is required to reopen the keyboard (and so the focus restored when
                // the modal closes does not immediately re-trigger this handler).
                Button saveButton = this.FindControl<Button>("btnSave");
                if (saveButton != null)
                {
                    saveButton.Focus();
                }
            }
            catch (Exception ex)
            {
                // [XPLAT] Defensive: an async-void handler must never crash the kiosk. Surface the failure
                // through the presenter when one is attached; otherwise degrade silently (the user can
                // still type with a physical keyboard via the bound entry).
                if (_errorPresenter != null)
                {
                    _errorPresenter.PresentTimedMessage(TimeSpan.FromSeconds(2), DialogTitle, ex.Message);
                }
            }
            finally
            {
                _keyboardShowing = false;
            }
        }

        // [XPLAT] QA F4-C2: the hand-written no-arg InitializeComponent() (which only called
        // AvaloniaXamlLoader.Load(this)) was removed so the constructor's InitializeComponent() binds to
        // the Avalonia source generator's InitializeComponent(bool loadXaml = true) overload, which loads
        // the XAML AND wires every x:Name control field. The manual no-arg overload shadowed the generated
        // one, leaving named-control fields null (the FormLoop/MainWindow lblIP NRE). — see TRANSITION_MAP.md
    }
}
