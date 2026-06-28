// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Avalonia code-behind for the field "Add Flag" dialog.
//
// 1:1 behavioural-parity reimplementation of the WinForms Forms/Field/FormEnterFlag
// (FormEnterFlag.cs + FormEnterFlag.Designer.cs). The dialog adds a flag at the current GPS position
// in one of three colours (red / green / yellow) and offers CSV import / export of the field's flags.
// Per AAP §0.3.3 this is faithful parity to the currently rendered Windows Forms UI — never a
// redesign: no new fields, no relayout, no UX changes. After a flag is added the dialog opens (or
// focuses) FormFlagsView (the WinForms original opened/focused FormFlags) and then closes.
//
// FRAMEWORK CONVERSIONS (each tagged // [XPLAT] below):
//   * System.Windows.Forms.Form              -> Avalonia.Controls.Window
//   * NudlessNumericUpDown (read-only spin)  -> read-only display Button whose tap opens the on-screen
//                                               numeric keypad FormNumeric (Views/Inputs); the value is
//                                               shown in Button.Content (a Button has no .Text)
//   * OpenFileDialog / SaveFileDialog         -> Avalonia TopLevel.StorageProvider OpenFilePicker /
//                                               SaveFilePicker (async), read/written via the picked
//                                               IStorageFile's stream
//   * FormDialog.Show(...)                    -> await FormDialogView.ShowAsync(title, msg, severity, this)
//   * Application.OpenForms["FormFlags"]      -> enumerate the desktop lifetime's open Windows for an
//                                               existing FormFlagsView (Avalonia has no OpenForms)
//   * every numeric .Parse/.ToString          -> CultureInfo.InvariantCulture (AAP §0.6.5 — the
//                                               cross-cutting data-integrity requirement; a comma-decimal
//                                               locale would otherwise corrupt the lat/lon/colour I/O)
//
// DEPENDENCY INJECTION (replaces the WinForms "mf" FormGPS god-object — NO FormGPS reference):
//   The WinForms form reached through a "private readonly FormGPS mf" back-reference for the flag
//   collection (mf.flagPts), the shared 1-based selection index (mf.flagNumberPicked), the application
//   model (mf.AppModel — for the current lat/lon and the WGS84->local-plane conversion) and the save
//   operation (mf.FileSaveFlags). Per the AAP guidance/scan-loop decoupling (§0.6.1, §0.3.2) this view
//   takes NO FormGPS reference: the composition root injects the SAME IFlagsViewContext that
//   FormFlagsView consumes (the live, shared flag state the OpenGL render loop also reads) plus the
//   ApplicationModel. Every flag read and write therefore goes THROUGH the injected context — never a
//   private copy — so a flag added here is immediately visible to the renderer and to an already-open
//   FormFlagsView, exactly as writing mf.flagPts / mf.flagNumberPicked was.
//
// No WinForms, no System.Drawing, no OpenTK, no GMap, no ColorPicker types are referenced.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AgLibrary.Logging;
using AgOpenGPS.Core;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.IO;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the field "Add Flag" dialog — the behaviour half of
    /// <c>FormEnterFlagView.axaml</c>. Faithful Avalonia reimplementation of the WinForms
    /// <c>FormEnterFlag</c>; an imperative dialog (no view-model, no data binding) that addresses every
    /// control by its original WinForms <c>x:Name</c> and wires every handler in the constructor.
    /// </summary>
    public partial class FormEnterFlagView : Window
    {
        // [XPLAT] The WinForms NudlessNumericUpDown ranges from FormEnterFlag.Designer.cs: latitude
        // +90..-90, longitude +180..-180. Used as the FormNumeric keypad bounds when a field is tapped.
        private const double LatitudeMinimum = -90.0;
        private const double LatitudeMaximum = 90.0;
        private const double LongitudeMinimum = -180.0;
        private const double LongitudeMaximum = 180.0;

        // [XPLAT] Seven-decimal display, matching the WinForms NudlessNumericUpDown DecimalPlaces = 7.
        // Purely cosmetic: the stored flag value is the raw double — CFlag rounds it to 7 dp internally —
        // so display formatting does not affect parity of the persisted flag.
        private const string CoordinateDisplayFormat = "0.0000000";

        /// <summary>
        /// The live, shared flag state / operations (replaces the WinForms <c>mf</c> FormGPS
        /// back-reference). This is the SAME context handed to <see cref="FormFlagsView"/>, so flags added
        /// here are visible to the render loop and to an already-open <see cref="FormFlagsView"/>.
        /// </summary>
        private readonly IFlagsViewContext _context;

        /// <summary>
        /// The application model (WinForms <c>mf.AppModel</c>); supplies the current lat/lon used to seed
        /// the fields and the <see cref="LocalPlane"/> used to convert WGS84 to the local easting/northing.
        /// </summary>
        private readonly ApplicationModel _appModel;

        /// <summary>Current latitude in degrees (WinForms <c>nudLatitude.Value</c>); seeds new flags.</summary>
        private double _latitude;

        /// <summary>Current longitude in degrees (WinForms <c>nudLongitude.Value</c>); seeds new flags.</summary>
        private double _longitude;

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's compiled-XAML loader. Without it the
        /// Avalonia XAML compiler raises AVLN3001 ("XAML resource ... won't be reachable via runtime
        /// loader, as no public constructor was found"), which the Release zero-warning gate forbids
        /// (see MIGRATION_DOCS/CHANGELOG.md, F1-001). The parity constructor below chains to it with
        /// <c>: this()</c> so <c>InitializeComponent()</c> runs exactly once.
        /// </summary>
        public FormEnterFlagView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initializes the dialog over the supplied live flag context and application model. Parity with
        /// the WinForms <c>FormEnterFlag(Form callingForm)</c> constructor, except the FormGPS "mf"
        /// reference is replaced by the injected <see cref="IFlagsViewContext"/> + <see cref="ApplicationModel"/>.
        /// Chains to the parameterless constructor (<c>: this()</c>) for <c>InitializeComponent()</c>; the
        /// dialog is only functional when constructed through this overload with the live flag state.
        /// </summary>
        /// <param name="context">The live, shared flag state and operations (must not be null).</param>
        /// <param name="appModel">The application model providing the current lat/lon and local plane (must not be null).</param>
        public FormEnterFlagView(IFlagsViewContext context, ApplicationModel appModel)
            : this()
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _appModel = appModel ?? throw new ArgumentNullException(nameof(appModel));

            // [XPLAT] WinForms ctor: this.Text = gStr.gsFormFlag; labelPoint.Text = gStr.gsPoint. The
            // .axaml carries only the design-time caption / label; the localised runtime strings are
            // assigned here.
            Title = gStr.gsFormFlag;
            labelPoint.Text = gStr.gsPoint;

            // [XPLAT] WinForms ctor: nudLatitude.Value = (decimal)mf.AppModel.CurrentLatLon.Latitude;
            // nudLongitude.Value = (decimal)mf.AppModel.CurrentLatLon.Longitude. Seed the live values and
            // their display text.
            _latitude = _appModel.CurrentLatLon.Latitude;
            _longitude = _appModel.CurrentLatLon.Longitude;
            UpdateCoordinateDisplays();

            // [XPLAT] The markup wires no handlers (only x:Name); attach them here by name. The two numeric
            // displays reproduce NudlessNumericUpDown.ShowKeypad (their Click opens the FormNumeric keypad).
            nudLatitude.Click += NudLatitude_Click;
            nudLongitude.Click += NudLongitude_Click;

            // [XPLAT] WinForms wired all three colour buttons to the single btnRed_Click handler; reproduce
            // that exact shared handler (it maps the pressed button to the colour byte red=0/green=1/yellow=2).
            btnRed.Click += FlagColor_Click;
            btnGreen.Click += FlagColor_Click;
            btnYellow.Click += FlagColor_Click;

            // [XPLAT] CSV import / export and cancel.
            btnImportFlags.Click += BtnImportFlags_Click;
            btnExportFlags.Click += BtnExportFlags_Click;
            btnCancel.Click += BtnCancel_Click;
        }

        /// <summary>
        /// [XPLAT] Writes the current latitude / longitude into the read-only display Buttons. Formatted
        /// with <see cref="CultureInfo.InvariantCulture"/> so the text is identical on every OS regardless
        /// of the active locale's decimal separator (AAP §0.6.5). A Button shows its value through
        /// <c>Content</c> (it has no <c>Text</c> property).
        /// </summary>
        private void UpdateCoordinateDisplays()
        {
            nudLatitude.Content = _latitude.ToString(CoordinateDisplayFormat, CultureInfo.InvariantCulture);
            nudLongitude.Content = _longitude.ToString(CoordinateDisplayFormat, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// [XPLAT] WinForms <c>nudLatitude_Click</c> -> <c>NudlessNumericUpDown.ShowKeypad(this)</c>. Opens
        /// the cross-platform on-screen numeric keypad <see cref="FormNumeric"/> (latitude range +90..-90)
        /// seeded with the current latitude; on accept, stores the result and refreshes the display.
        /// <c>async void</c> is the established Avalonia event-handler shape for awaiting a modal dialog.
        /// </summary>
        /// <param name="sender">The latitude display button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void NudLatitude_Click(object sender, RoutedEventArgs e)
        {
            FormNumeric keypad = new FormNumeric(LatitudeMinimum, LatitudeMaximum, _latitude);
            if (await keypad.ShowDialog<bool>(this))
            {
                _latitude = keypad.ReturnValue;
                UpdateCoordinateDisplays();
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>nudLongitude_Click</c> -> <c>NudlessNumericUpDown.ShowKeypad(this)</c>. Opens
        /// <see cref="FormNumeric"/> (longitude range +180..-180) seeded with the current longitude; on
        /// accept, stores the result and refreshes the display.
        /// </summary>
        /// <param name="sender">The longitude display button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void NudLongitude_Click(object sender, RoutedEventArgs e)
        {
            FormNumeric keypad = new FormNumeric(LongitudeMinimum, LongitudeMaximum, _longitude);
            if (await keypad.ShowDialog<bool>(this))
            {
                _longitude = keypad.ReturnValue;
                UpdateCoordinateDisplays();
            }
        }

        /// <summary>
        /// [XPLAT] Port of the single WinForms <c>btnRed_Click</c> handler shared by all three colour
        /// buttons: maps the pressed button to a colour byte (red = 0, green = 1, yellow = 2), builds a
        /// <see cref="CFlag"/> at the current lat/lon (converted to the local plane), adds it to the live
        /// shared collection, de-duplicates and persists, then opens — or, if one is already open, focuses —
        /// <see cref="FormFlagsView"/> and closes this dialog.
        /// </summary>
        /// <param name="sender">The pressed colour button (btnRed / btnGreen / btnYellow).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void FlagColor_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] WinForms branched on the button Name; comparing to the named fields is equivalent and
            // robust. WinForms initialised flagColor = 0, so btnRed (and any fallback) is red.
            byte flagColor = 0;
            if (ReferenceEquals(sender, btnGreen))
            {
                flagColor = 1;
            }
            else if (ReferenceEquals(sender, btnYellow))
            {
                flagColor = 2;
            }

            // [XPLAT] mf.AppModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84((double)nudLatitude.Value, ...)).
            GeoCoord geoCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(_latitude, _longitude));

            // [XPLAT] nextflag is the pre-add count + 1 (computed exactly as the WinForms handler did) and is
            // used both as the CFlag ID and (stringified, InvariantCulture) as its default note.
            int nextflag = _context.FlagPts.Count + 1;
            CFlag flagPt = new CFlag(
                _latitude, _longitude,
                geoCoord.Easting, geoCoord.Northing,
                0, flagColor, nextflag, nextflag.ToString(CultureInfo.InvariantCulture));

            _context.FlagPts.Add(flagPt);
            ReconcileDeduplicatedFlags();
            _context.FileSaveFlags();

            // [XPLAT] WinForms: Form fc = Application.OpenForms["FormFlags"]; if (fc != null) { fc.Focus();
            // return; }. Avalonia has no OpenForms — enumerate the desktop lifetime's open Windows. If a
            // FormFlagsView is already open, focus it and leave this dialog open (do NOT open a second one).
            FormFlagsView existingFlagsView = FindOpenFlagsView();
            if (existingFlagsView != null)
            {
                existingFlagsView.Activate();
                return;
            }

            // [XPLAT] WinForms: if (mf.flagPts.Count > 0) { mf.flagNumberPicked = nextflag;
            // new FormFlags(mf).Show(mf); }  Close();
            if (_context.FlagPts.Count > 0)
            {
                _context.FlagNumberPicked = nextflag;

                FormFlagsView flagsView = new FormFlagsView(_context);

                // [XPLAT] WinForms showed FormFlags owned by the main form (mf), NOT by this dialog, so it
                // survives this dialog closing. Own it by this dialog's owner (the main window); fall back to
                // an unowned show if no owner is set so Close() below never tears the new window down.
                Window flagsOwner = Owner as Window;
                if (flagsOwner != null)
                {
                    flagsView.Show(flagsOwner);
                }
                else
                {
                    flagsView.Show();
                }
            }

            Close();
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnImportFlags_Click</c>: load flags from a text/CSV file. Replaces the
        /// WinForms <c>OpenFileDialog</c> with Avalonia's <see cref="IStorageProvider"/> file picker, then
        /// reproduces the original parse exactly — skip the header line, split each line on commas, parse
        /// latitude / longitude (<see cref="CultureInfo.InvariantCulture"/>, <see cref="NumberStyles.Float"/>)
        /// and colour, build a <see cref="CFlag"/> via the local plane, add it, de-duplicate and persist.
        /// A malformed line shows an error dialog and stops; any exception shows an error dialog and logs.
        /// </summary>
        /// <param name="sender">The import button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void BtnImportFlags_Click(object sender, RoutedEventArgs e)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            // [XPLAT] OpenFileDialog (Filter "Text Document|*.txt|CSV Document|*.csv", Multiselect = false,
            // Title "Please select points file") -> StorageProvider OpenFilePicker.
            IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Please select points file",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Text/CSV")
                    {
                        // [XPLAT] both cases listed for case-sensitive (Linux/macOS) filesystems.
                        Patterns = new[] { "*.txt", "*.csv", "*.TXT", "*.CSV" }
                    }
                }
            });

            // [XPLAT] WinForms: if (fileDialog.ShowDialog() != DialogResult.OK) do nothing.
            if (files == null || files.Count == 0)
            {
                return;
            }

            try
            {
                // [XPLAT] System.IO.File.ReadAllLines(filePath) -> read every line from the picked file's
                // stream. ReadLine semantics are used (not a Split on the whole text) so a trailing newline
                // does not produce a phantom empty line that the parse below would reject — matching
                // File.ReadAllLines exactly. leaveOpen keeps the single `await using` stream the sole owner.
                List<string> allLines = new List<string>();
                await using (Stream stream = await files[0].OpenReadAsync())
                using (StreamReader reader = new StreamReader(stream, leaveOpen: true))
                {
                    string fileLine;
                    while ((fileLine = await reader.ReadLineAsync()) != null)
                    {
                        allLines.Add(fileLine);
                    }
                }

                // [XPLAT] foreach (string line in lines.Skip(1)) — skip the header row.
                foreach (string line in allLines.Skip(1))
                {
                    string[] parts = line.Split(',');
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double latitude) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double longitude) &&
                        int.TryParse(parts[2], out int flagColor))
                    {
                        string flagName = (!string.IsNullOrWhiteSpace(parts[3]))
                            ? parts[3].Trim()
                            : $"{_context.FlagPts.Count + 1}";
                        GeoCoord geoCoord = _appModel.LocalPlane.ConvertWgs84ToGeoCoord(new Wgs84(latitude, longitude));
                        int nextflag = _context.FlagPts.Count + 1;
                        CFlag flagPt = new CFlag(
                            latitude, longitude,
                            geoCoord.Easting, geoCoord.Northing,
                            0, flagColor, nextflag, flagName);
                        _context.FlagPts.Add(flagPt);
                        ReconcileDeduplicatedFlags();
                        _context.FileSaveFlags();
                    }
                    else
                    {
                        // [XPLAT] FormDialog.Show("Error check format", ...) -> FormDialogView.ShowAsync.
                        await FormDialogView.ShowAsync("Error check format", $"Invalid line: {line}", DialogSeverity.Error, this);
                        return;
                    }
                }

                // [XPLAT] FormDialog.Show("Success", "Flags successfully added!", Info).
                await FormDialogView.ShowAsync("Success", "Flags successfully added!", DialogSeverity.Info, this);
            }
            catch (Exception ex)
            {
                // [XPLAT] FormDialog.Show("Error", ...) + Log.EventWriter, verbatim from the WinForms catch.
                await FormDialogView.ShowAsync("Error", $"Error reading file: {ex.Message}", DialogSeverity.Error, this);
                Log.EventWriter("Loading Flags by lat lon" + ex.ToString());
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnExportFlags_Click</c>: export the field's flags to a CSV file. Replaces
        /// the WinForms <c>SaveFileDialog</c> with Avalonia's <see cref="IStorageProvider"/> save picker,
        /// then writes the header row followed by one
        /// <c>latitude,longitude,colour,notes</c> row per flag — every numeric field formatted with
        /// <see cref="CultureInfo.InvariantCulture"/>. Success and failure both surface through
        /// <see cref="FormDialogView"/>; failures are also logged.
        /// </summary>
        /// <param name="sender">The export button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private async void BtnExportFlags_Click(object sender, RoutedEventArgs e)
        {
            TopLevel topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            // [XPLAT] SaveFileDialog (DefaultExt "txt", Filter txt/csv, Title "Export flags information")
            // -> StorageProvider SaveFilePicker.
            IStorageFile file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export flags information",
                DefaultExtension = "txt",
                SuggestedFileName = "Flags",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Text/CSV")
                    {
                        Patterns = new[] { "*.txt", "*.csv" }
                    }
                }
            });

            // [XPLAT] WinForms: if (fileDialog.ShowDialog() != DialogResult.OK) do nothing.
            if (file == null)
            {
                return;
            }

            try
            {
                // [XPLAT] new StreamWriter(fileName) (truncating) -> write through the picked file's stream.
                // OpenWriteAsync may not truncate when overwriting a larger existing file, so reset the
                // length first; leaveOpen lets the inner writer flush/close while the `await using` stream
                // remains the sole owner that disposes the handle.
                await using (Stream stream = await file.OpenWriteAsync())
                {
                    if (stream.CanSeek)
                    {
                        stream.SetLength(0);
                    }

                    using (StreamWriter writer = new StreamWriter(stream, leaveOpen: true))
                    {
                        writer.WriteLine("Latitude,Longitude,Color,Notes");

                        foreach (CFlag flag in _context.FlagPts)
                        {
                            writer.WriteLine(
                                flag.latitude.ToString(CultureInfo.InvariantCulture) + "," +
                                flag.longitude.ToString(CultureInfo.InvariantCulture) + "," +
                                flag.color.ToString(CultureInfo.InvariantCulture) + "," +
                                flag.notes);
                        }
                    }
                }

                // [XPLAT] FormDialog.Show("Success", "Flags successfully saved!", Info).
                await FormDialogView.ShowAsync("Success", "Flags successfully saved!", DialogSeverity.Info, this);
            }
            catch (Exception ex)
            {
                // [XPLAT] FormDialog.Show("Error", ex.Message + "\nCannot write to file.") + Log.EventWriter.
                await FormDialogView.ShowAsync("Error", ex.Message + "\nCannot write to file.", DialogSeverity.Error, this);
                Log.EventWriter("Saving Flags by lat lon" + ex.ToString());
            }
        }

        /// <summary>
        /// [XPLAT] WinForms <c>btnCancel_Click</c>: close the dialog (the WinForms button carried
        /// DialogResult.Cancel and called Close()).
        /// </summary>
        /// <param name="sender">The cancel button (unused).</param>
        /// <param name="e">The routed event data (unused).</param>
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Reproduces the WinForms <c>mf.flagPts = FlagsFiles.DeduplicateFlags(mf.flagPts)</c>
        /// reassignment. <see cref="FlagsFiles.DeduplicateFlags"/> returns a NEW list, but the injected
        /// <see cref="IFlagsViewContext.FlagPts"/> is read-only by contract (it must remain the same live
        /// instance the render loop and FormFlagsView draw), so the de-duplicated result is applied by
        /// mutating that shared list in place rather than replacing the reference — observably identical
        /// for every state while keeping the shared instance intact.
        /// </summary>
        private void ReconcileDeduplicatedFlags()
        {
            List<CFlag> deduplicated = FlagsFiles.DeduplicateFlags(_context.FlagPts);
            if (!ReferenceEquals(deduplicated, _context.FlagPts))
            {
                _context.FlagPts.Clear();
                _context.FlagPts.AddRange(deduplicated);
            }
        }

        /// <summary>
        /// [XPLAT] Avalonia replacement for <c>Application.OpenForms["FormFlags"]</c>: returns the first
        /// open <see cref="FormFlagsView"/> from the classic-desktop application lifetime's window list, or
        /// <see langword="null"/> when none is open.
        /// </summary>
        /// <returns>The already-open <see cref="FormFlagsView"/>, or <see langword="null"/>.</returns>
        private static FormFlagsView FindOpenFlagsView()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (Window window in desktop.Windows)
                {
                    if (window is FormFlagsView flagsView)
                    {
                        return flagsView;
                    }
                }
            }

            return null;
        }
    }
}
