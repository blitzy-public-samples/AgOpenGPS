// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// [XPLAT] Avalonia code-behind for the AgShare field-DOWNLOAD dialog — a 1:1 behavioural-parity
// reimplementation of the WinForms Forms/Field/FormAgShareDownloader (FormAgShareDownloader.cs +
// FormAgShareDownloader.Designer.cs, 334 lines). The dialog lists the user's own AgShare fields on
// the left, previews the selected field's boundary / AB-line / curve geometry on the right, and
// offers download controls (get-selected, get-all with a progress bar, force-overwrite toggle).
//
// Cross-platform conversions ([XPLAT]):
//   * OpenTK.GLControl + immediate-mode OpenGL (glControl1 / GL.Ortho / GL.Begin / GL.Vertex2 /
//     LineStipple / SwapBuffers) -> a native Avalonia DrawingContext preview control injected into
//     the previewHost Border. The preview is non-operational (not the live field view), so a 2D
//     DrawingContext renderer is the robust, fully cross-platform choice and avoids the GL-context
//     -type risk entirely. See FieldPreviewControl at the bottom of this file.
//   * WinForms ListView / ListViewItem / SelectedIndexChanged -> Avalonia ListBox / GetOwnFieldDto
//     rows (the .axaml ItemTemplate binds {Binding Name}) / SelectionChanged.
//   * CheckBox.Checked -> IsChecked; Control.Visible -> IsVisible; Control.Enabled -> IsEnabled;
//     Label.ForeColor -> TextBlock.Foreground; FormDialog.Show -> FormDialogView.ShowAsync.
//   * Every integer rendered into a message uses CultureInfo.InvariantCulture so a comma-decimal
//     locale on Linux/macOS can never drift the text (AAP cross-cutting culture rule §0.6.5).
//   * NO System.Windows.Forms / System.Drawing / OpenTK / GMap imports; NO FormGPS "gps"/"mf"
//     god-object — the four things the WinForms ctor reached off gps (the AgShare client, the
//     isJobStarted flag, and the save/open-field callbacks) plus the fields directory are injected
//     discretely through the constructor.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AgOpenGPS.Classes.AgShare.Helpers;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.AgShare.Models;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Dialog that lets the operator preview and download their own AgShare fields, rendering
    /// boundaries, AB lines and curves with a native Avalonia <see cref="DrawingContext"/> preview.
    /// Imperative code-behind (NO DataContext / x:DataType / MVVM bindings); controls are addressed by
    /// <c>x:Name</c> and the handlers are wired here because the markup declares none.
    /// </summary>
    /// <remarks>
    /// The WinForms <c>FormAgShareDownloader(FormGPS gpsContext)</c> reached <c>gps.agShareClient</c>,
    /// <c>gps.isJobStarted</c>, <c>gps.FileSaveEverythingBeforeClosingField()</c> and
    /// <c>gps.FileOpenField(...)</c>; those four collaborators (plus the fields directory) are injected
    /// discretely so this view never depends on the FormGPS god-object. The shared
    /// <see cref="AgShareDownloader"/> server logic is reused verbatim and behaviourally frozen.
    /// </remarks>
    public partial class FormAgShareDownloaderView : Window
    {
        // [XPLAT] Injected collaborators replacing the WinForms "gps" back-reference (all readonly).
        private readonly AgShareDownloader _downloader;
        private readonly bool _isJobStarted;
        private readonly Func<Task> _fileSaveEverythingBeforeClosingField;
        private readonly Func<string, Task> _fileOpenField;
        private readonly string _fieldsDirectory;
        private readonly Window _owner;

        // [XPLAT] Native preview surface injected into the previewHost Border (replaces glControl1).
        private readonly FieldPreviewControl _preview = new FieldPreviewControl();

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader (its absence
        /// raises AVLN3001, a fatal warning under the Release <c>TreatWarningsAsErrors</c> build). It is
        /// also the single construction chokepoint: the injected constructor chains here with
        /// <c>: this()</c>, so the markup-free handler wiring, the preview attachment, the initial
        /// control state and the localized captions are applied exactly once.
        /// </summary>
        public FormAgShareDownloaderView()
        {
            InitializeComponent();

            // [XPLAT] Attach the native DrawingContext preview as the previewHost child. The original
            // FormAgShareDownloader_Load cleared glControl1 to a dark colour; an empty field renders a
            // blank dark preview here, so the surface always reads correctly before a field is selected.
            previewHost.Child = _preview;

            // [XPLAT] Initial visibility — verbatim from the WinForms constructor (Visible = false). The
            // .axaml already starts these hidden; re-asserting here keeps the chokepoint authoritative.
            progressBarDownloadAll.IsVisible = false;
            lblDownloading.IsVisible = false;

            // [XPLAT] Localized captions / title from gStr — verbatim from the WinForms constructor. The
            // button / checkbox captions are unnamed TextBlocks nested in the StackPanel Content, so they
            // are set through SetCaption rather than a direct .Text assignment.
            SetCaption(chkForceOverwrite, gStr.gsForceOverwrite);
            SetCaption(btnSaveAll, gStr.gsDownloadAll);
            SetCaption(btnGetSelected, gStr.gsGetSelected);
            lblDownloading.Text = $"{gStr.gsDownloading}... {gStr.gsPleaseWait}";
            Title = gStr.gsAgShareDownloader;

            // [XPLAT] The markup declares no handlers; wire the imperative behaviour by name here.
            lbFields.SelectionChanged += lbFields_SelectionChanged;
            btnGetSelected.Click += btnGetSelected_Click;
            btnSaveAll.Click += btnSaveAll_Click;
            btnClose.Click += btnClose_Click;

            // [XPLAT] WinForms Form.Load (FormAgShareDownloader_Load) -> Avalonia Window.Opened.
            Opened += OnViewOpened;
        }

        /// <summary>
        /// [XPLAT] Behavioural-parity constructor replacing the WinForms
        /// <c>FormAgShareDownloader(FormGPS gpsContext)</c>. The collaborators the original read off the
        /// god-object are injected discretely; the shared <see cref="AgShareDownloader"/> is created from
        /// the injected client exactly as the original did (<c>new AgShareDownloader(gps.agShareClient)</c>).
        /// </summary>
        /// <param name="agShareClient">The AgShare network client (was <c>gps.agShareClient</c>).</param>
        /// <param name="isJobStarted">Whether a field job is currently open (was <c>gps.isJobStarted</c>).</param>
        /// <param name="fileSaveEverythingBeforeClosingField">
        /// Callback that saves the open field before closing it (was
        /// <c>gps.FileSaveEverythingBeforeClosingField()</c>).</param>
        /// <param name="fileOpenField">Callback that opens a field by its <c>Field.txt</c> path (was
        /// <c>gps.FileOpenField(...)</c>).</param>
        /// <param name="fieldsDirectory">The local fields root (was <c>RegistrySettings.fieldsDirectory</c>).</param>
        /// <param name="owner">The owning window, used as the modal owner for child dialogs.</param>
        public FormAgShareDownloaderView(
            AgShareClient agShareClient,
            bool isJobStarted,
            Func<Task> fileSaveEverythingBeforeClosingField,
            Func<string, Task> fileOpenField,
            string fieldsDirectory,
            Window owner)
            : this()
        {
            _downloader = new AgShareDownloader(agShareClient);
            _isJobStarted = isJobStarted;
            _fileSaveEverythingBeforeClosingField = fileSaveEverythingBeforeClosingField;
            _fileOpenField = fileOpenField;
            _fieldsDirectory = fieldsDirectory;
            _owner = owner;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>FormAgShareDownloader_Load</c>: load the user's own fields from the AgShare
        /// server and populate <c>lbFields</c>. Each row's backing object is the <see cref="GetOwnFieldDto"/>
        /// itself (the .axaml ItemTemplate shows <c>Name</c>); the first row is selected when any exist.
        /// </summary>
        private async void OnViewOpened(object sender, EventArgs e)
        {
            // The parameterless constructor is only used by the XAML loader, which never opens the window;
            // guard so a (theoretical) parameterless open cannot dereference a null downloader.
            if (_downloader == null)
            {
                return;
            }

            try
            {
                List<GetOwnFieldDto> fields = await _downloader.GetOwnFieldsAsync();

                if (fields == null)
                {
                    await FormDialogView.ShowAsync("AgShare", "Failed to load field list.", DialogSeverity.Error, _owner);
                    return;
                }

                lbFields.Items.Clear();
                foreach (GetOwnFieldDto field in fields)
                {
                    // [XPLAT] Add the DTO directly; the ListBox ItemTemplate binds {Binding Name} and the
                    // selection is read back as a GetOwnFieldDto (replaces ListViewItem { Tag = field }).
                    lbFields.Items.Add(field);
                }

                if (lbFields.Items.Count > 0)
                {
                    lbFields.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                await FormDialogView.ShowAsync("AgShare", "Failed to load field list.\n" + ex.Message, DialogSeverity.Error, _owner);
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>lbFields_SelectedIndexChanged</c>: download a preview of the selected field
        /// and render it in the native preview. The selected-field label is recoloured red, mirroring the
        /// original <c>lblSelectedField.ForeColor = Color.Red</c>.
        /// </summary>
        private async void lbFields_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(lbFields.SelectedItem is GetOwnFieldDto dto))
            {
                return;
            }

            lblSelectedField.Text = "Selected Field: " + dto.Name;
            lblSelectedField.Foreground = Brushes.Red;

            // Download and parse the field for preview (server logic frozen).
            GetFieldDto previewDto = await _downloader.DownloadFieldPreviewAsync(dto.Id);

            if (previewDto == null)
            {
                await FormDialogView.ShowAsync("AgShare", "Failed to download field preview. Check logs for details.", DialogSeverity.Error, _owner);
                return;
            }

            // Already converted to NE inside the parser; assigning Field invalidates the preview.
            ParsedField localModel = AgShareFieldParser.Parse(previewDto);
            _preview.Field = localModel;
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnOpen_Click</c> (the "Get Selected" button): download + save the selected
        /// field, then open it. If a job is already started the open field is saved/closed first via the
        /// injected callback, exactly as the original called <c>gps.FileSaveEverythingBeforeClosingField()</c>.
        /// </summary>
        private async void btnGetSelected_Click(object sender, RoutedEventArgs e)
        {
            if (lbFields.SelectedItem == null)
            {
                await FormDialogView.ShowAsync("AgShare", "No field selected.", DialogSeverity.Error, _owner);
                return;
            }

            if (!(lbFields.SelectedItem is GetOwnFieldDto selected))
            {
                await FormDialogView.ShowAsync("AgShare", "Invalid selection.", DialogSeverity.Error, _owner);
                return;
            }

            // Attempt to download and save the field locally (server logic frozen).
            bool success = await _downloader.DownloadAndSaveAsync(selected.Id);
            if (!success)
            {
                await FormDialogView.ShowAsync("AgShare", "Failed to download field.", DialogSeverity.Error, _owner);
                return;
            }

            // [XPLAT] Build the full path to Field.txt from the injected fields directory (was
            // RegistrySettings.fieldsDirectory) using Path.Combine for cross-platform separators.
            string fieldDir = Path.Combine(_fieldsDirectory, selected.Name);
            string fieldFile = Path.Combine(fieldDir, "Field.txt");

            if (!File.Exists(fieldFile))
            {
                await FormDialogView.ShowAsync("AgShare", "Field saved but could not be opened (missing Field.txt).", DialogSeverity.Error, _owner);
                return;
            }

            // Close the current field if necessary, then open the downloaded field.
            if (_isJobStarted && _fileSaveEverythingBeforeClosingField != null)
            {
                await _fileSaveEverythingBeforeClosingField();
            }

            if (_fileOpenField != null)
            {
                await _fileOpenField(fieldFile);
            }

            Close();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>BtnDownloadAll_Click</c> (the "Get All" button): download every own field
        /// with a progress bar, disabling the command controls for the duration. The progress callback is
        /// a cross-platform <see cref="Progress{T}"/> (the WinForms <c>progressBar.Refresh()</c> is dropped —
        /// Avalonia repaints automatically). A try/finally guarantees the controls are always re-enabled,
        /// fixing the original's latent stuck-UI defect on a mid-download exception (graceful degradation,
        /// AAP §0.7.2).
        /// </summary>
        private async void btnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            // Disable the UI during the download.
            lblDownloading.IsVisible = true;
            btnSaveAll.IsEnabled = false;
            btnClose.IsEnabled = false;
            btnGetSelected.IsEnabled = false;
            chkForceOverwrite.IsEnabled = false;
            progressBarDownloadAll.IsVisible = true;
            progressBarDownloadAll.Value = 0;

            string message;
            DialogSeverity severity;
            try
            {
                // Determine the maximum for the progress bar.
                List<GetOwnFieldDto> fields = await _downloader.GetOwnFieldsAsync();
                progressBarDownloadAll.Maximum = fields?.Count ?? 0;

                // Report progress straight onto the bar's Value (Avalonia repaints automatically).
                var progress = new Progress<int>(v => { progressBarDownloadAll.Value = v; });
                bool forceOverwrite = chkForceOverwrite.IsChecked == true;

                var result = await _downloader.DownloadAllAsync(forceOverwrite, progress);

                // Build the result message (integers formatted invariantly).
                message = $"Downloaded {result.Downloaded.ToString(CultureInfo.InvariantCulture)} new field(s).";
                if (result.Skipped > 0)
                {
                    message += $"\nSkipped {result.Skipped.ToString(CultureInfo.InvariantCulture)} existing.";
                }
                if (result.Failed > 0)
                {
                    message += $"\nFailed {result.Failed.ToString(CultureInfo.InvariantCulture)} field(s).";
                }
                severity = DialogSeverity.Info;
            }
            catch (Exception ex)
            {
                message = "Failed to download fields.\n" + ex.Message;
                severity = DialogSeverity.Error;
            }
            finally
            {
                // Restore the UI regardless of outcome.
                progressBarDownloadAll.IsVisible = false;
                lblDownloading.IsVisible = false;
                btnClose.IsEnabled = true;
                btnGetSelected.IsEnabled = true;
                btnSaveAll.IsEnabled = true;
                chkForceOverwrite.IsEnabled = true;
            }

            await FormDialogView.ShowAsync("AgShare", message, severity, _owner);
        }

        /// <summary>[XPLAT] WinForms <c>btnClose_Click</c>: close the dialog.</summary>
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Set the caption of a flat image-above-text command control (the WinForms
        /// <c>control.Text = ...</c>). The caption is the first <see cref="TextBlock"/> in the control's
        /// StackPanel Content (declared in the .axaml); the leading <see cref="Image"/> glyph is left
        /// untouched. A null caption is ignored so the design-time text remains as a sensible fallback.
        /// </summary>
        private static void SetCaption(ContentControl host, string text)
        {
            if (text == null)
            {
                return;
            }

            if (host?.Content is Panel panel)
            {
                foreach (Control child in panel.Children)
                {
                    if (child is TextBlock textBlock)
                    {
                        textBlock.Text = text;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// [XPLAT] RenderField re-expressed from immediate-mode OpenGL to an Avalonia
        /// <see cref="DrawingContext"/> (non-operational preview). Reproduces the WinForms
        /// <c>FormAgShareDownloader.RenderField</c> exactly: an anthracite background, lime-green closed
        /// boundary loops, and dashed track lines (red for curves, orange for AB lines), projected with the
        /// same <c>GetBoundingBox</c> + margin + orthographic mapping the original fed to <c>GL.Ortho</c>.
        /// </summary>
        private sealed class FieldPreviewControl : Control
        {
            // [XPLAT] Exact parity palette from RenderField. Source GL values -> Avalonia colours:
            //   GL.ClearColor(0.12,0.12,0.12,1)  -> #FF1F1F1F  (anthracite background)
            //   GL.Color4(0,1,0,0.8)             -> #CC00FF00  (boundary, lime green, alpha 204)
            //   GL.Color4(1,0,0,0.9)             -> #E6FF0000  (curve, red, alpha 230)
            //   GL.Color4(1,0.65,0,0.9)          -> #E6FFA600  (AB line, orange, alpha 230)
            // Tracks were stroked at GL.LineWidth(3.5) with LineStipple(1, 0x0F0F); the dashed pens below
            // approximate that stipple with a 2-on / 2-off DashStyle. The boundary used the default GL line
            // width (no LineWidth call before the loop), drawn here at a visible thickness of 2.
            private static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.FromRgb(31, 31, 31));
            private static readonly IPen BoundaryPen = new Pen(new SolidColorBrush(Color.FromArgb(204, 0, 255, 0)), 2.0);
            private static readonly IPen CurvePen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 0, 0)), 3.5, new DashStyle(new double[] { 2.0, 2.0 }, 0.0));
            private static readonly IPen AbPen = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 166, 0)), 3.5, new DashStyle(new double[] { 2.0, 2.0 }, 0.0));

            private ParsedField _field;

            /// <summary>The field to preview; assigning it repaints the surface.</summary>
            public ParsedField Field
            {
                get => _field;
                set
                {
                    _field = value;
                    InvalidateVisual();
                }
            }

            /// <summary>
            /// [XPLAT] Paint the preview. Mirrors RenderField: fill the anthracite background, then (if a
            /// field is set) stroke the boundaries as closed loops and the tracks as dashed lines, mapping
            /// world (easting, northing) to control pixels with northing pointing up.
            /// </summary>
            public override void Render(DrawingContext context)
            {
                base.Render(context);

                double width = Bounds.Width;
                double height = Bounds.Height;
                if (width <= 0.0 || height <= 0.0)
                {
                    return;
                }

                // Anthracite background (source GL.ClearColor(0.12, 0.12, 0.12, 1)).
                context.FillRectangle(BackgroundBrush, new Rect(0.0, 0.0, width, height));

                ParsedField field = _field;
                if (field == null)
                {
                    return;
                }

                // Determine scaling based on boundary extents, or tracks if no boundary (parity with the
                // GetBoundingBox + margin the original passed to GL.Ortho).
                GeoBoundingBox fieldBb = GetBoundingBox(field.Boundaries, field.Tracks);

                // Ensure non-zero margins even for vertical/horizontal lines or single points.
                GeoDelta bbMargin = new GeoDelta(
                    Math.Max(0.05 * (fieldBb.MaxNorthing - fieldBb.MinNorthing), 50),
                    Math.Max(0.05 * (fieldBb.MaxEasting - fieldBb.MinEasting), 50));
                GeoBoundingBox bbWithMargin = new GeoBoundingBox(fieldBb.MinCoord - bbMargin, fieldBb.MaxCoord + bbMargin);

                double minEasting = bbWithMargin.MinEasting;
                double minNorthing = bbWithMargin.MinNorthing;
                double spanEasting = bbWithMargin.MaxEasting - minEasting;
                double spanNorthing = bbWithMargin.MaxNorthing - minNorthing;

                // Guard degenerate spans (the margins above make this practically impossible, but a zero
                // span would divide by zero in the linear map below).
                if (spanEasting <= 0.0)
                {
                    spanEasting = 1.0;
                }
                if (spanNorthing <= 0.0)
                {
                    spanNorthing = 1.0;
                }

                // Draw field boundaries as closed lime-green loops (source GL LineLoop per fenceLine).
                if (field.Boundaries != null)
                {
                    foreach (CBoundaryList bnd in field.Boundaries)
                    {
                        if (bnd?.fenceLine == null || bnd.fenceLine.Count < 2)
                        {
                            continue;
                        }

                        var geometry = new StreamGeometry();
                        using (StreamGeometryContext geo = geometry.Open())
                        {
                            vec3 start = bnd.fenceLine[0];
                            geo.BeginFigure(MapToPixel(start.easting, start.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height), false);
                            for (int i = 1; i < bnd.fenceLine.Count; i++)
                            {
                                vec3 pt = bnd.fenceLine[i];
                                geo.LineTo(MapToPixel(pt.easting, pt.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height));
                            }
                            geo.EndFigure(true);
                        }

                        context.DrawGeometry(null, BoundaryPen, geometry);
                    }
                }

                // Draw AB lines and curves (dashed): curves red, AB lines orange.
                if (field.Tracks != null)
                {
                    foreach (CTrk trk in field.Tracks)
                    {
                        if (trk == null)
                        {
                            continue;
                        }

                        if (trk.mode == TrackMode.Curve && trk.curvePts != null && trk.curvePts.Count > 0)
                        {
                            if (trk.curvePts.Count < 2)
                            {
                                continue;
                            }

                            var geometry = new StreamGeometry();
                            using (StreamGeometryContext geo = geometry.Open())
                            {
                                vec3 start = trk.curvePts[0];
                                geo.BeginFigure(MapToPixel(start.easting, start.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height), false);
                                for (int i = 1; i < trk.curvePts.Count; i++)
                                {
                                    vec3 pt = trk.curvePts[i];
                                    geo.LineTo(MapToPixel(pt.easting, pt.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height));
                                }
                                geo.EndFigure(false);
                            }

                            context.DrawGeometry(null, CurvePen, geometry);
                        }
                        else
                        {
                            Point a = MapToPixel(trk.ptA.easting, trk.ptA.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height);
                            Point b = MapToPixel(trk.ptB.easting, trk.ptB.northing, minEasting, minNorthing, spanEasting, spanNorthing, width, height);
                            context.DrawLine(AbPen, a, b);
                        }
                    }
                }
            }

            // [XPLAT] Linear map from world (easting, northing) within the margin-padded bounding box to a
            // control pixel. Matches the raw GL.Ortho rectangle the original used: easting spans the full
            // width and northing spans the full height (no aspect preservation), with Y flipped so northing
            // points up (the GL viewport had its origin at the bottom-left).
            private static Point MapToPixel(
                double easting, double northing,
                double minEasting, double minNorthing,
                double spanEasting, double spanNorthing,
                double width, double height)
            {
                double x = (easting - minEasting) / spanEasting * width;
                double y = height - (northing - minNorthing) / spanNorthing * height;
                return new Point(x, y);
            }

            // [XPLAT] Ported VERBATIM from FormAgShareDownloader.GetBoundingBox: prefer boundary extents,
            // fall back to track extents (curve points, else A/B endpoints), and finally to a +/-100 box.
            private static GeoBoundingBox GetBoundingBox(List<CBoundaryList> boundaries, List<CTrk> tracks)
            {
                GeoBoundingBox bb = GeoBoundingBox.CreateEmpty();

                // Check boundaries first
                if (boundaries != null)
                {
                    foreach (var bnd in boundaries)
                    {
                        foreach (var pt in bnd.fenceLine)
                        {
                            bb.Include(pt.ToGeoCoord());
                        }
                    }
                }

                // If no boundary, use tracks to calculate bounds
                if (bb.IsEmpty && tracks != null)
                {
                    foreach (var trk in tracks)
                    {
                        if (trk.mode == TrackMode.Curve && trk.curvePts != null && trk.curvePts.Count > 0)
                        {
                            foreach (var pt in trk.curvePts)
                            {
                                bb.Include(pt.ToGeoCoord());
                            }
                        }
                        else
                        {
                            bb.Include(trk.ptA.ToGeoCoord());
                            bb.Include(trk.ptB.ToGeoCoord());
                        }
                    }
                }

                // Fallback to default bounds if no data
                if (bb.IsEmpty)
                {
                    bb.Include(new GeoCoord(-100.0, -100.0));
                    bb.Include(new GeoCoord(100.0, 100.0));
                }
                return bb;
            }
        }
    }
}
