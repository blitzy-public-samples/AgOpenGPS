// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.Interfaces;
using Avalonia.Controls;

namespace AgOpenGPS.Views
{
    // [XPLAT] IPanelPresenter aggregate; holds Avalonia Select-Field + Config-Menu sub-presenters; wired by App.axaml.cs (composition root) replacing FormGPS null-wiring (dir,null,null).
    /// <summary>
    /// Avalonia implementation of the Core <see cref="IPanelPresenter"/> aggregate contract. It is a thin
    /// composition root that bundles the two panel sub-presenters the Core view-models consume —
    /// <see cref="AvaloniaSelectFieldPanelPresenter"/> (the Select-Field flow) and
    /// <see cref="AvaloniaConfigMenuPanelPresenter"/> (the Config-Menu flow) — and surfaces them through
    /// the <see cref="ISelectFieldPanelPresenter"/> and <see cref="IConfigMenuPanelPresenter"/> interface
    /// properties.
    /// </summary>
    /// <remarks>
    /// <para>
    /// [XPLAT] This is the single object that <c>App.axaml.cs</c> passes as the <c>panelPresenter</c>
    /// argument to <c>new ApplicationCore(baseDir, panelPresenter, errorPresenter)</c>, replacing the
    /// legacy WinForms null-wiring where <c>FormGPS</c> constructed <c>ApplicationCore(dir, null, null)</c>
    /// with no real presenters. The Core <c>ApplicationPresenter</c> reaches the field-selection and
    /// configuration dialogs exclusively through <see cref="SelectFieldPanelPresenter"/> and
    /// <see cref="ConfigMenuPanelPresenter"/>, so this aggregate is the seam that lets the portable Core
    /// drive the Avalonia UI without any reference to a concrete window type.
    /// </para>
    /// <para>
    /// The class performs no UI work itself and references no <c>System.Windows.Forms</c> type; it is pure
    /// aggregation. The lifetime, threading (UI-thread marshalling) and modal-versus-modeless presentation
    /// of the actual dialogs live entirely in the two sub-presenters. The preferred
    /// <see cref="AvaloniaPanelPresenter(AvaloniaSelectFieldPanelPresenter, AvaloniaConfigMenuPanelPresenter)"/>
    /// constructor takes the concrete sub-presenters so the composition root can keep its own references
    /// and assign their parent <see cref="Window"/> after <c>MainView</c> has been built; the convenience
    /// <see cref="AvaloniaPanelPresenter()"/> constructor creates both internally for callers (and tests)
    /// that do not need that hand-off.
    /// </para>
    /// </remarks>
    public class AvaloniaPanelPresenter : IPanelPresenter
    {
        /// <summary>
        /// The concrete Select-Field sub-presenter. Stored as the concrete type (rather than the
        /// <see cref="ISelectFieldPanelPresenter"/> interface) so the <see cref="Owner"/> forwarder can set
        /// its parent window, which is a concrete-class member not part of the Core interface.
        /// </summary>
        private readonly AvaloniaSelectFieldPanelPresenter _selectFieldPanelPresenter;

        /// <summary>
        /// The concrete Config-Menu sub-presenter. Stored as the concrete type for the same
        /// <see cref="Owner"/>-forwarding reason as <see cref="_selectFieldPanelPresenter"/>.
        /// </summary>
        private readonly AvaloniaConfigMenuPanelPresenter _configMenuPanelPresenter;

        /// <summary>
        /// Initializes a new <see cref="AvaloniaPanelPresenter"/> from already-constructed sub-presenters.
        /// This is the preferred (dependency-injected) form: <c>App.axaml.cs</c> creates the two concrete
        /// presenters, passes them here, and retains the ability to set their parent window — either by
        /// holding its own references or through the <see cref="Owner"/> forwarder on this aggregate.
        /// </summary>
        /// <param name="selectFieldPanelPresenter">The Select-Field flow sub-presenter. Must not be
        /// <see langword="null"/>.</param>
        /// <param name="configMenuPanelPresenter">The Config-Menu flow sub-presenter. Must not be
        /// <see langword="null"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when either sub-presenter is
        /// <see langword="null"/>, since the Core view-models would otherwise dereference a missing
        /// presenter.</exception>
        public AvaloniaPanelPresenter(
            AvaloniaSelectFieldPanelPresenter selectFieldPanelPresenter,
            AvaloniaConfigMenuPanelPresenter configMenuPanelPresenter)
        {
            _selectFieldPanelPresenter = selectFieldPanelPresenter
                ?? throw new ArgumentNullException(nameof(selectFieldPanelPresenter));
            _configMenuPanelPresenter = configMenuPanelPresenter
                ?? throw new ArgumentNullException(nameof(configMenuPanelPresenter));
        }

        /// <summary>
        /// Initializes a new <see cref="AvaloniaPanelPresenter"/> that creates both sub-presenters
        /// internally. This convenience overload chains to
        /// <see cref="AvaloniaPanelPresenter(AvaloniaSelectFieldPanelPresenter, AvaloniaConfigMenuPanelPresenter)"/>;
        /// it is useful for callers that do not need to retain the concrete sub-presenter references (the
        /// shared parent window can still be supplied in one call through the <see cref="Owner"/> setter).
        /// </summary>
        public AvaloniaPanelPresenter()
            : this(new AvaloniaSelectFieldPanelPresenter(), new AvaloniaConfigMenuPanelPresenter())
        {
        }

        /// <inheritdoc />
        public ISelectFieldPanelPresenter SelectFieldPanelPresenter => _selectFieldPanelPresenter;

        /// <inheritdoc />
        public IConfigMenuPanelPresenter ConfigMenuPanelPresenter => _configMenuPanelPresenter;

        /// <summary>
        /// [XPLAT] Convenience accessor that forwards a single parent <see cref="Window"/> to both
        /// sub-presenters in one assignment, so <c>App.axaml.cs</c> can establish the dialog owner once
        /// after <c>MainView</c> is built. Setting it assigns the same owner to the Select-Field and
        /// Config-Menu presenters; getting it returns that shared owner (read from the Select-Field
        /// presenter, which always carries the same value because both are set together here).
        /// </summary>
        /// <value>
        /// The window that parents (owns) the field-selection and configuration dialogs, or
        /// <see langword="null"/> when no owner has been assigned yet — in which case the sub-presenters
        /// fall back to modeless presentation.
        /// </value>
        public Window Owner
        {
            get => _selectFieldPanelPresenter.Owner;
            set
            {
                _selectFieldPanelPresenter.Owner = value;
                _configMenuPanelPresenter.Owner = value;
            }
        }
    }
}
