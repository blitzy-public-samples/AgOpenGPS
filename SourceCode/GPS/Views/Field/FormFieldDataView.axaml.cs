// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using AgOpenGPS.Core.Translations;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "System Data" field-work statistics read-out — a 1:1 behavioural
    /// parity port of the WinForms <c>FormFieldData</c> (Forms/Field/FormFieldData.cs +
    /// FormFieldData.Designer.cs). It is a narrow, modeless panel (shown via <c>Show(owner)</c>) that
    /// displays live field-work statistics — worked / applied area, remaining area and estimated time,
    /// overlap percentage, work rate, and trip area / distance — refreshed once per second.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative view: NO <c>DataContext</c>, NO <c>x:DataType</c>, NO MVVM bindings — every control
    /// is addressed by its <c>x:Name</c>, matching the paired <c>FormFieldDataView.axaml</c> contract
    /// and the accepted sibling code-behind dialogs (e.g. <c>FormTimedMessageView</c>). The single
    /// declared handler <see cref="btnTripReset_Click"/> is wired from the .axaml.
    /// </para>
    /// <para>
    /// [XPLAT] The WinForms shell reached the field-data model through the <c>FormGPS</c> god-object
    /// (<c>mf.fd</c>, <c>mf.bnd</c>, <c>mf.isMetric</c>). That back-reference is removed here: the three
    /// collaborators the refresh actually uses are constructor-injected instead — the field-data stats
    /// object (<see cref="CFieldData"/>), the boundary model (<see cref="CBoundary"/>, queried only for
    /// <c>bndList.Count</c>), and the metric/imperial flag. No <c>FormGPS</c>/<c>mf</c> reference is
    /// held.
    /// </para>
    /// <para>
    /// [XPLAT] The WinForms <c>System.Windows.Forms.Timer</c> (<c>timer1</c>, <c>Interval = 1000</c> ms)
    /// is reproduced with a <see cref="DispatcherTimer"/>. The original <c>FormFieldData_Load</c> called
    /// <c>timer1_Tick</c> once immediately; that is reproduced in <see cref="OnLoaded"/>, which performs
    /// the first refresh and then starts the timer. The timer is deterministically stopped and detached
    /// in <see cref="OnClosed"/> so it cannot fire after the window is gone.
    /// </para>
    /// <para>
    /// Every <c>CFieldData</c> member consumed here is a pre-formatted <see cref="string"/> property
    /// (already culture-formatted by the behaviour-frozen Core/Classes layer); the values are assigned
    /// to <c>.Text</c> verbatim and are never re-formatted here.
    /// </para>
    /// </remarks>
    public partial class FormFieldDataView : Window
    {
        // [XPLAT] Constructor-injected collaborators replacing the former FormGPS (mf) back-reference.
        // _fd supplies the pre-formatted statistic strings and the resettable trip totals; _bnd is
        // queried only for bndList.Count to gate the "Remain" group; _isMetric selects the unit branch.
        // They are null only for a loader/designer-constructed instance (the parameterless ctor below).
        private readonly CFieldData _fd;
        private readonly CBoundary _bnd;

        // [XPLAT] The original read mf.isMetric on every tick. It is supplied here as a value per the
        // injection contract and stored once; RefreshData still reads this field on each refresh so the
        // metric/imperial branch is evaluated per tick exactly as the original timer1_Tick did.
        private readonly bool _isMetric;

        // [XPLAT] Replaces System.Windows.Forms.Timer timer1 (Interval = 1000 ms). Created only by the
        // dependency-injected ctor (a data-less loader/designer instance has nothing to refresh), started
        // in OnLoaded, and stopped/detached in OnClosed.
        private DispatcherTimer _timer;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia XAML loader / design-time preview
        /// (matching the accepted sibling views). Production code uses the dependency-injected overload
        /// below; an instance created here has no data source, so the per-second refresh and the
        /// trip-reset safely no-op until collaborators are supplied. The caption labels are seeded from
        /// <see cref="gStr"/> (static, dependency-free) so the panel reads correctly under the active
        /// language in both the designer and at runtime — exactly as the WinForms constructor did.
        /// </summary>
        public FormFieldDataView()
        {
            InitializeComponent();

            // Caption labels are (re)assigned from the translation table exactly as the WinForms
            // constructor did, so the panel reads correctly under any active language. The verbatim
            // English text in the .axaml is the design-time / no-translation fallback these overwrite.
            labelTotal.Text = gStr.gsTotal + ":";
            labelWorked.Text = gStr.gsWorked;
            labelApplied.Text = gStr.gsApplied + ":";
            labelApplied2.Text = gStr.gsApplied + ":";
            labelRemain.Text = gStr.gsRemain + ":";
            labelRemain2.Text = gStr.gsRemain + ":";
            labelOverlap.Text = gStr.gsOverlap + ":";
            labelActual.Text = gStr.gsActual;
            labelRate.Text = gStr.gsRate + ":";
            labelArea.Text = gStr.gsArea + ":";
            labelDistance.Text = gStr.gsDistance + ":";
        }

        /// <summary>
        /// Creates the read-out bound to its live collaborators (the production constructor).
        /// </summary>
        /// <param name="fd">
        /// The field-work statistics model whose pre-formatted string properties populate the value
        /// labels and whose trip totals the reset button zeroes. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="bnd">
        /// The boundary model; only <c>bndList.Count</c> is consulted, to show or hide the boundary-gated
        /// "Remain" group. Must not be <see langword="null"/>.
        /// </param>
        /// <param name="isMetric">
        /// <see langword="true"/> to display hectares / metres, <see langword="false"/> to display acres
        /// / feet — mirroring <c>mf.isMetric</c>.
        /// </param>
        public FormFieldDataView(CFieldData fd, CBoundary bnd, bool isMetric)
            : this()
        {
            // [XPLAT] Validate the injected collaborators up-front (the WinForms form trusted mf to be
            // present); fail fast rather than throwing a NullReferenceException on the first refresh.
            _fd = fd ?? throw new ArgumentNullException(nameof(fd));
            _bnd = bnd ?? throw new ArgumentNullException(nameof(bnd));
            _isMetric = isMetric;

            // [XPLAT] timer1: Enabled = true, Interval = 1000. Started in OnLoaded (after the first
            // refresh) rather than at construction so no tick fires before the window is shown.
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _timer.Tick += OnTimerTick;
        }

        /// <summary>
        /// [XPLAT] Replaces the WinForms <c>FormFieldData_Load</c> handler: performs the first refresh
        /// immediately (the original called <c>timer1_Tick</c> on load) and then starts the per-second
        /// timer.
        /// </summary>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            RefreshData();

            // Guard against a redundant restart should OnLoaded be raised more than once for this
            // top-level window. (_timer is null for a loader/designer instance, which never ticks.)
            if (_timer != null && !_timer.IsEnabled)
            {
                _timer.Start();
            }
        }

        // [XPLAT] DispatcherTimer.Tick adapter for the per-second refresh (was timer1.Tick).
        private void OnTimerTick(object sender, EventArgs e)
        {
            RefreshData();
        }

        /// <summary>
        /// [XPLAT] Faithful port of the WinForms <c>timer1_Tick</c>: copies the pre-formatted statistic
        /// strings off <see cref="CFieldData"/> into the value labels (selecting metric or imperial),
        /// and shows the boundary-gated "Remain" group only when a field boundary exists.
        /// </summary>
        private void RefreshData()
        {
            // No data source — only possible for a loader/designer-constructed instance — so there is
            // nothing to read. Production instances always supply the collaborators via the injected ctor.
            if (_fd == null || _bnd == null)
            {
                return;
            }

            lblOverlapPercent.Text = _fd.ActualOverlapPercent;

            if (_isMetric)
            {
                lblWorkRate.Text = _fd.WorkRateHectares;
                lblApplied.Text = _fd.WorkedHectares;
                lblActualLessOverlap.Text = _fd.ActualAreaWorkedHectares;
                labelAreaValue.Text = _fd.WorkedUserHectares + " ha";
                labelDistanceDriven.Text = _fd.DistanceUserMeters + " m";
            }
            else
            {
                lblWorkRate.Text = _fd.WorkRateAcres;
                lblApplied.Text = _fd.WorkedAcres;
                lblActualLessOverlap.Text = _fd.ActualAreaWorkedAcres;
                labelAreaValue.Text = _fd.WorkedUserAcres + " ac";
                labelDistanceDriven.Text = _fd.DistanceUserFeet + " ft";
            }

            // [XPLAT] The "Remain" group is meaningful only with a field boundary to measure against
            // (mf.bnd.bndList.Count > 0). .Visible -> .IsVisible.
            if (_bnd.bndList.Count > 0)
            {
                lblTimeRemaining.Text = _fd.TimeTillFinished;
                lblRemainPercent.Text = _fd.WorkedAreaRemainPercentage;
                lblTotalArea.IsVisible = true;
                lblAreaRemain.IsVisible = true;
                lblTimeRemaining.IsVisible = true;
                lblRemainPercent.IsVisible = true;
                labelRemain.IsVisible = true;
                lblActualRemain.IsVisible = true;
                labelRemain2.IsVisible = true;

                if (_isMetric)
                {
                    lblTotalArea.Text = _fd.AreaBoundaryLessInnersHectares;
                    lblAreaRemain.Text = _fd.WorkedAreaRemainHectares;
                    lblActualRemain.Text = _fd.ActualRemainHectares;
                }
                else
                {
                    lblTotalArea.Text = _fd.AreaBoundaryLessInnersAcres;
                    lblAreaRemain.Text = _fd.WorkedAreaRemainAcres;
                    lblActualRemain.Text = _fd.ActualRemainAcres;
                }
            }
            else
            {
                lblTotalArea.IsVisible = false;
                lblAreaRemain.IsVisible = false;
                lblTimeRemaining.IsVisible = false;
                lblRemainPercent.IsVisible = false;
                lblActualRemain.IsVisible = false;
                labelRemain2.IsVisible = false;
                labelRemain.IsVisible = false;
            }
        }

        // [XPLAT] btnTripReset_Click: zero the trip totals on the field-data model exactly as the
        // WinForms handler did (mf.fd.workedAreaTotalUser = 0; mf.fd.distanceUser = 0). The next refresh
        // reflects the reset. Guarded for the data-less loader/designer instance.
        private void btnTripReset_Click(object sender, RoutedEventArgs e)
        {
            if (_fd == null)
            {
                return;
            }

            _fd.workedAreaTotalUser = 0;
            _fd.distanceUser = 0;
        }

        /// <summary>
        /// [XPLAT] Deterministically stops and detaches the <see cref="DispatcherTimer"/> so it cannot
        /// fire after the window is closed (the WinForms timer was disposed with the form's components).
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }
    }
}
