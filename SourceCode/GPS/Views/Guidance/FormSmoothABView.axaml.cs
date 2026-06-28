// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Provenance summary for this code-behind:
//   * WinForms System.Windows.Forms.Form  -> Avalonia.Controls.Window (Forms/Guidance/FormSmoothAB.cs
//     + FormSmoothAB.Designer.cs are the 1:1 source; behaviour is frozen, only the host stack changes).
//   * The central FormGPS ("mf") god-object is removed. The three things the original touched —
//     mf.curve (the shared CABCurve), mf.oglMain.Refresh() (repaint the main GL viewport) and
//     mf.FileSaveTracks() (persist the tracks) — become constructor-injected collaborators: a real
//     CABCurve plus two Action callbacks (AAP §0.3.2 "Extract Class / inject collaborators").
//   * The original AgLibrary.Controls.RepeatButton raised repeated MouseDown while held; the Avalonia
//     built-in RepeatButton raises Click repeatedly while held — the correct parity mapping. Only
//     RepeatButton.Click is wired (NOT PointerPressed) so the auto-repeat does not double-fire.
//   * The smoothing maths, persistence and the smooth-window flag all live on the injected CABCurve
//     exactly as the original (curve.SmoothAB / SaveSmoothList / smooList / isSmoothWindowOpen), so no
//     behaviour is reimplemented here — this view only drives the existing, behaviour-frozen domain.
using System;
using System.Globalization;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views;

/// <summary>
/// [XPLAT] Code-behind for the Smooth-AB-Curve dialog — a faithful 1:1 behavioural port of the
/// WinForms <c>FormSmoothAB</c> (<c>Forms/Guidance/FormSmoothAB.cs</c> +
/// <c>FormSmoothAB.Designer.cs</c>). The operator raises / lowers a smoothing window with the North /
/// South repeat-buttons and watches the live preview, then keeps the result "for now"
/// (<c>bntOK</c>), saves it "to file" (<c>btnSave</c>), or discards it (<c>btnCancel</c>).
/// </summary>
/// <remarks>
/// <para>
/// This is an IMPERATIVE dialog — there is intentionally no <c>DataContext</c>, no <c>x:DataType</c>
/// and no MVVM data binding. The <c>x:Name</c>'d controls declared in <c>FormSmoothABView.axaml</c>
/// (<c>btnNorth</c>, <c>btnSouth</c>, <c>lblSmooth</c>, <c>bntOK</c>, <c>btnSave</c>,
/// <c>btnCancel</c>, and the in-button caption <c>TextBlock</c>s <c>bntOKText</c> / <c>btnSaveText</c>)
/// are the entire integration surface; the markup wires no handlers, so the constructor attaches them
/// programmatically. The handlers keep the original WinForms method names for traceability
/// (<c>btnNorth_Click</c> / <c>btnSouth_Click</c> / <c>bntOK_Click</c> / <c>btnSave_Click</c> /
/// <c>btnCancel_Click</c>).
/// </para>
/// <para>
/// The original consumed the <c>FormGPS</c> ("mf") god-object directly (<c>mf.curve</c>,
/// <c>mf.oglMain.Refresh()</c>, <c>mf.FileSaveTracks()</c>). Per AAP §0.3.2 those are replaced by
/// explicit constructor injection: the shared <see cref="CABCurve"/> the dialog smooths, plus two
/// <see cref="Action"/> callbacks — <see cref="_refreshView"/> (repaint the main viewport) and
/// <see cref="_saveTracks"/> (persist the tracks). This view therefore holds no reference to
/// <c>FormGPS</c> and reimplements none of the smoothing maths; it only drives the behaviour-frozen
/// <see cref="CABCurve"/>.
/// </para>
/// </remarks>
public partial class FormSmoothABView : Window
{
    // [XPLAT] was mf.curve — the shared curve this dialog re-smooths and (on commit) persists.
    private readonly CABCurve _curve;

    // [XPLAT] was mf.oglMain.Refresh() — invalidate / repaint the main AvaloniaGeoViewport so the
    // operator sees the freshly-smoothed curve after each adjustment. Supplied by the composition root.
    private readonly Action _refreshView;

    // [XPLAT] was mf.FileSaveTracks() — persist the whole track collection to disk ("To File"). Supplied
    // by the composition root.
    private readonly Action _saveTracks;

    // [XPLAT] Smoothing counter — identical seed and clamp range [2, 100] to the original FormSmoothAB
    // (FormSmoothAB.cs: private int smoothCount = 20;). The actual smoothing window passed to the domain
    // is smoothCount * 2, exactly as the source.
    private int smoothCount = 20;

    /// <summary>
    /// Parameterless constructor required by Avalonia's compiled-XAML runtime loader so the
    /// <c>avares://AgOpenGPS/Views/Guidance/FormSmoothABView.axaml</c> resource stays reachable
    /// (otherwise the build emits warning <c>AVLN3001</c>, which the Release configuration treats as an
    /// error). Production code constructs the dialog via
    /// <see cref="FormSmoothABView(CABCurve, Action, Action)"/>.
    /// </summary>
    public FormSmoothABView()
    {
        // [XPLAT] InitializeComponent is emitted by the Avalonia XAML source generator from
        // FormSmoothABView.axaml; it also creates the typed x:Name'd control fields this code-behind uses.
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog, mirroring the WinForms <c>FormSmoothAB(Form callingForm)</c> constructor:
    /// it stores the injected collaborators, applies the localized captions from <see cref="gStr"/>, and
    /// wires the button handlers (the markup declares none).
    /// </summary>
    /// <param name="curve">
    /// The shared curve to smooth — the cross-platform replacement for the original <c>mf.curve</c>.
    /// </param>
    /// <param name="refreshView">
    /// Repaints the main field viewport after each change — the replacement for
    /// <c>mf.oglMain.Refresh()</c>. May be <see langword="null"/> (invoked null-safely).
    /// </param>
    /// <param name="saveTracks">
    /// Persists the track collection to disk for the "To File" path — the replacement for
    /// <c>mf.FileSaveTracks()</c>. May be <see langword="null"/> (invoked null-safely).
    /// </param>
    public FormSmoothABView(CABCurve curve, Action refreshView, Action saveTracks)
        : this()
    {
        _curve = curve;
        _refreshView = refreshView;
        _saveTracks = saveTracks;

        // [XPLAT] Translations: identical assignments to the FormSmoothAB constructor
        // (this.bntOK.Text = gStr.gsForNow; this.btnSave.Text = gStr.gsToFile; this.Text = gStr.gsSmoothABCurve;).
        // An Avalonia Button has no .Text — its caption lives in an Image-over-text Content stack — so the
        // localisable captions are set on the dedicated in-button TextBlocks (bntOKText / btnSaveText).
        bntOKText.Text = gStr.gsForNow;
        btnSaveText.Text = gStr.gsToFile;
        Title = gStr.gsSmoothABCurve;

        // [XPLAT] Designer-wired events reproduced programmatically (the .axaml declares none). The
        // North / South arrows are RepeatButtons: Avalonia raises Click repeatedly while held, matching
        // the original AgLibrary.Controls.RepeatButton MouseDown auto-repeat. Wiring lives in this
        // injected ctor (not the parameterless one) so a design-time / loader instance leaves the buttons
        // inert and the handlers always run with a non-null _curve.
        btnNorth.Click += btnNorth_Click;
        btnSouth.Click += btnSouth_Click;
        bntOK.Click += bntOK_Click;
        btnSave.Click += btnSave_Click;
        btnCancel.Click += btnCancel_Click;
    }

    /// <summary>
    /// [XPLAT] Ports <c>FormSmoothAB_Load</c>: marks the smooth window open on the curve, resets the
    /// counter to its seed and shows the "**" placeholder. The original <c>ScreenHelper.IsOnScreen</c>
    /// reposition is omitted because <c>WindowStartupLocation="CenterOwner"</c> handles placement.
    /// </summary>
    /// <param name="e">The event payload.</param>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // [XPLAT] mf.curve.isSmoothWindowOpen = true. Null-guarded for the parameterless (loader) path,
        // where no curve was injected; production always opens via the injected ctor.
        if (_curve != null)
        {
            _curve.isSmoothWindowOpen = true;
        }

        smoothCount = 20;
        lblSmooth.Text = "**";
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnNorth_MouseDown</c>: increases the smoothing window (post-increment compare
    /// clamps it at 100), re-smooths the curve and repaints the viewport. The exact
    /// post-increment-then-compare semantics of the source are preserved.
    /// </summary>
    /// <param name="sender">The North repeat-button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnNorth_Click(object sender, RoutedEventArgs e)
    {
        if (smoothCount++ > 100) smoothCount = 100;
        _curve.SmoothAB(smoothCount * 2);
        // [XPLAT] InvariantCulture keeps the rendered digits stable across OS locales (culture-safety).
        lblSmooth.Text = smoothCount.ToString(CultureInfo.InvariantCulture);
        _refreshView?.Invoke();
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnSouth_MouseDown</c>: decreases the smoothing window (clamped at 2), re-smooths
    /// the curve and repaints the viewport.
    /// </summary>
    /// <param name="sender">The South repeat-button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnSouth_Click(object sender, RoutedEventArgs e)
    {
        smoothCount--;
        if (smoothCount < 2) smoothCount = 2;
        _curve.SmoothAB(smoothCount * 2);
        // [XPLAT] InvariantCulture keeps the rendered digits stable across OS locales (culture-safety).
        lblSmooth.Text = smoothCount.ToString(CultureInfo.InvariantCulture);
        _refreshView?.Invoke();
    }

    /// <summary>
    /// [XPLAT] Ports <c>bntOK_Click</c> ("For Now"): closes the smooth window, commits the working
    /// smooth list to the curve, clears the working buffer and closes the dialog — keeping the smoothing
    /// for this session only.
    /// </summary>
    /// <param name="sender">The "For Now" button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void bntOK_Click(object sender, RoutedEventArgs e)
    {
        _curve.isSmoothWindowOpen = false;
        _curve.SaveSmoothList();
        _curve.smooList?.Clear();
        Close();
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnSave_Click</c> ("To File"): closes the smooth window, commits the working
    /// smooth list to the curve, clears the working buffer, persists the tracks to disk
    /// (<see cref="_saveTracks"/>), repaints the viewport (<see cref="_refreshView"/>) and closes.
    /// </summary>
    /// <param name="sender">The "To File" button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        _curve.isSmoothWindowOpen = false;
        _curve.SaveSmoothList();
        _curve.smooList?.Clear();

        // [XPLAT] was mf.FileSaveTracks() — persist the entire track list.
        _saveTracks?.Invoke();

        // [XPLAT] was mf.oglMain.Refresh() — repaint the main view to show the updated curve.
        _refreshView?.Invoke();

        Close();
    }

    /// <summary>
    /// [XPLAT] Ports <c>btnCancel_Click</c>: closes the smooth window, discards the working smooth list
    /// and closes the dialog without committing or persisting anything.
    /// </summary>
    /// <param name="sender">The Cancel button; unused.</param>
    /// <param name="e">The routed-event payload; unused.</param>
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        _curve.isSmoothWindowOpen = false;
        _curve.smooList?.Clear();
        Close();
    }

    /// <summary>
    /// [XPLAT] Defensive reset: the WinForms form hid its control box, but Avalonia cannot suppress only
    /// the title-bar close button, so the dialog can also be dismissed via the window chrome. Clearing
    /// <c>isSmoothWindowOpen</c> here guarantees the curve's smooth-window flag is always reset on close,
    /// regardless of which path closed the dialog. Idempotent with the commit/cancel handlers.
    /// </summary>
    /// <param name="e">The event payload.</param>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Null-guarded for the parameterless (loader) path, where no curve was injected.
        if (_curve != null)
        {
            _curve.isSmoothWindowOpen = false;
        }
    }
}
