// [XPLAT] migrated from net48/WinForms FormSource.cs + FormSource.Designer.cs — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using AgOpenGPS.Core.ViewModels;

namespace AgIO.Views
{
    /// <summary>
    /// A single parsed row of the NTRIP caster source-table, as shown in the
    /// <c>FormSource</c> mountpoint picker. One <see cref="SourceEntry"/> is produced per
    /// <c>STR;</c> record handed to <see cref="FormSourceViewModel"/>; the original WinForms
    /// dialog rendered the same fields as the six columns of its <c>lvLines</c>
    /// <c>ListView</c> (Distance, Mount Point, Lat, Lon, Format, Network).
    /// </summary>
    /// <remarks>
    /// This is an immutable-by-convention display record: the view-model fully populates it
    /// during construction and never mutates it afterwards. <see cref="DistanceKm"/> carries
    /// the numeric great-circle distance (in kilometres) used for sorting, while
    /// <see cref="DistanceText"/> carries the pre-formatted, culture-invariant display string
    /// shown to the operator.
    /// </remarks>
    public sealed class SourceEntry
    {
        /// <summary>
        /// Gets or sets the mountpoint name (the caster <c>STR</c> record's first field).
        /// This is the value returned to the caller when the operator confirms a choice.
        /// </summary>
        public string Mount { get; set; }

        /// <summary>
        /// Gets or sets the mountpoint latitude, as the raw trimmed text from the source-table
        /// record (kept as text to display exactly what the caster reported).
        /// </summary>
        public string Latitude { get; set; }

        /// <summary>
        /// Gets or sets the mountpoint longitude, as the raw trimmed text from the source-table
        /// record (kept as text to display exactly what the caster reported).
        /// </summary>
        public string Longitude { get; set; }

        /// <summary>
        /// Gets or sets the correction data format (e.g. <c>RTCM 3.2</c>) reported by the caster.
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Gets or sets the network/identifier field reported by the caster.
        /// </summary>
        public string Network { get; set; }

        /// <summary>
        /// Gets or sets a single combined, human-readable summary of the non-distance,
        /// non-mountpoint fields (latitude, longitude, format, network) for views that prefer a
        /// compact two/three-column layout over the original six-column grid.
        /// </summary>
        public string Info { get; set; }

        /// <summary>
        /// Gets or sets the pre-formatted distance string shown in the list. Formatted with
        /// <see cref="CultureInfo.InvariantCulture"/> using the original <c>"#######"</c> mask,
        /// right-padded to ten characters, exactly matching the WinForms presentation.
        /// </summary>
        public string DistanceText { get; set; }

        /// <summary>
        /// Gets or sets the numeric great-circle distance to the mountpoint, in kilometres.
        /// Used as the sort key for the "nearest first" ordering. Entries whose caster did not
        /// report coordinates carry the original <c>9999999.9</c> sentinel so they sort last.
        /// </summary>
        public double DistanceKm { get; set; }
    }

    /// <summary>
    /// View-model backing <c>FormSource.axaml</c>, the NTRIP caster source-table (mountpoint)
    /// picker that replaces the WinForms <c>FormSource</c>. It is opened by <c>FormNtrip</c> so
    /// the operator can choose a mountpoint from the caster's parsed source-table, ranked by the
    /// great-circle distance from the operator's current position to each base.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Dialog result (god-object removal).</b> The original dialog wrote the chosen mountpoint
    /// directly back into the calling <c>FormNtrip.tboxMount.Text</c>. That back-reference is
    /// removed: this view-model instead exposes the chosen value and raises
    /// <see cref="RequestClose"/> with the selected mountpoint string (or
    /// <see cref="string.Empty"/> when nothing is chosen / on cancel). The hosting window closes
    /// the dialog with that string via <c>ShowDialog&lt;string&gt;</c>; <c>FormNtrip</c> reads the
    /// returned value and updates its own <c>Mount</c> property.
    /// </para>
    /// <para>
    /// <b>Culture safety.</b> Every numeric parse and format uses
    /// <see cref="CultureInfo.InvariantCulture"/>, exactly as the WinForms original did, so that a
    /// locale whose decimal separator is a comma can never corrupt the latitude/longitude parsing
    /// or the distance display (AAP §0.6.5).
    /// </para>
    /// <para>
    /// <b>Behaviour-frozen math.</b> <see cref="GetDistance"/> is ported verbatim from the
    /// original (identical haversine formula and Earth radius), so distances are bit-for-bit
    /// equivalent to the Windows build.
    /// </para>
    /// </remarks>
    public class FormSourceViewModel : ViewModel
    {
        // The "no coordinates reported" distance sentinel from the WinForms original. Entries the
        // caster could not geolocate are pushed to the bottom of the nearest-first ordering.
        private const double UnknownDistanceKm = 9999999.9;

        // Operator position (decimal degrees) used as the reference point for every distance.
        private readonly double _lat;
        private readonly double _lon;

        // The caster's site/home URL opened by the "visit website" action. Never null.
        private readonly string _site;

        private SourceEntry _selectedEntry;
        private bool _isSortedByDistance;

        /// <summary>
        /// Initializes a new instance of the <see cref="FormSourceViewModel"/> class, parsing the
        /// caster source-table into the ranked <see cref="Entries"/> collection.
        /// </summary>
        /// <param name="dataList">
        /// The caster source-table records, one comma-delimited <c>STR</c> record per element
        /// (mountpoint, latitude, longitude, format, network, ...). A <c>null</c> list yields an
        /// empty <see cref="Entries"/> collection.
        /// </param>
        /// <param name="lat">The operator's current latitude in decimal degrees.</param>
        /// <param name="lon">The operator's current longitude in decimal degrees.</param>
        /// <param name="site">
        /// The caster's site/home URL opened by <see cref="VisitSiteCommand"/>. May be empty.
        /// </param>
        public FormSourceViewModel(List<string> dataList, double lat, double lon, string site)
        {
            _lat = lat;
            _lon = lon;
            // Guard against a null URL without using a nullable annotation (nullable refs are
            // disabled project-wide); the "visit website" action treats empty as a no-op.
            _site = site ?? string.Empty;

            // Default ordering is "nearest first", matching the original ListView's ascending
            // sort on the (distance) first column.
            _isSortedByDistance = true;

            Entries = new ObservableCollection<SourceEntry>();

            OkCommand = new RelayCommand(OnOk);
            CancelCommand = new RelayCommand(OnCancel);
            VisitSiteCommand = new RelayCommand(OnVisitSite);
            SortCommand = new RelayCommand(OnSort);

            BuildEntries(dataList);
            ApplySort();
        }

        /// <summary>
        /// Gets the ranked collection of parsed source-table rows bound to the list in the view
        /// (an Avalonia <c>ListBox</c>/<c>DataGrid</c> replacing the WinForms <c>lvLines</c>).
        /// </summary>
        public ObservableCollection<SourceEntry> Entries { get; }

        /// <summary>
        /// Gets or sets the row currently selected in the list. Setting it also refreshes
        /// <see cref="SelectedMount"/>.
        /// </summary>
        public SourceEntry SelectedEntry
        {
            get { return _selectedEntry; }
            set
            {
                if (value != _selectedEntry)
                {
                    _selectedEntry = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(SelectedMount));
                }
            }
        }

        /// <summary>
        /// Gets the mountpoint name of the current selection, or <see cref="string.Empty"/> when
        /// nothing is selected. Implemented as a guarded (non-nullable) read because nullable
        /// reference types are disabled project-wide.
        /// </summary>
        public string SelectedMount
        {
            get { return _selectedEntry != null ? _selectedEntry.Mount : string.Empty; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the list is ordered by distance ("nearest
        /// first"). When <c>false</c>, the list is ordered alphabetically by mountpoint name. This
        /// mirrors the original <c>btnSort</c> toggle and lets the view reflect the active mode.
        /// </summary>
        public bool IsSortedByDistance
        {
            get { return _isSortedByDistance; }
            set
            {
                if (value != _isSortedByDistance)
                {
                    _isSortedByDistance = value;
                    NotifyPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the command that confirms the operator's choice. Raises <see cref="RequestClose"/>
        /// with the selected mountpoint, or <see cref="string.Empty"/> when nothing is selected.
        /// </summary>
        public ICommand OkCommand { get; }

        /// <summary>
        /// Gets the command that dismisses the dialog without a choice. Raises
        /// <see cref="RequestClose"/> with <see cref="string.Empty"/>.
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// Gets the command that opens the caster's site/home URL in the operating system's
        /// default handler. Best-effort: a failure (no handler, malformed URL) is a silent no-op.
        /// </summary>
        public ICommand VisitSiteCommand { get; }

        /// <summary>
        /// Gets the command that toggles the list ordering between distance ("nearest first") and
        /// mountpoint name, reproducing the WinForms <c>btnSort</c> behaviour.
        /// </summary>
        public ICommand SortCommand { get; }

        /// <summary>
        /// Raised when the dialog should close. The argument carries the chosen mountpoint name,
        /// or <see cref="string.Empty"/> when the operator cancelled or made no selection. The
        /// hosting window subscribes and closes with this value (via <c>ShowDialog&lt;string&gt;</c>).
        /// Initialized to a no-op delegate so it is always safe to invoke.
        /// </summary>
        public event Action<string> RequestClose = delegate { };

        /// <summary>
        /// Parses the caster source-table into <see cref="Entries"/>. Each record is split on
        /// commas into mountpoint, latitude, longitude, format and network fields; the
        /// great-circle distance to the operator is computed (in kilometres) and pre-formatted for
        /// display. Records the caster did not geolocate (zero latitude or longitude) receive the
        /// <see cref="UnknownDistanceKm"/> sentinel so they sort last — exactly as the original.
        /// </summary>
        /// <param name="dataList">The caster source-table records; <c>null</c> is treated as empty.</param>
        private void BuildEntries(List<string> dataList)
        {
            if (dataList == null)
            {
                return;
            }

            foreach (string line in dataList)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] data = line.Split(',');

                // The original indexed fields 0..4 directly; guard malformed records (fewer than
                // five fields) so a single bad line cannot crash the picker. Well-formed STR
                // records always carry these fields, so this is behaviour-identical for valid data.
                if (data.Length < 5)
                {
                    continue;
                }

                // [XPLAT] InvariantCulture parsing preserved verbatim from the WinForms original so
                // a comma-decimal locale cannot misread the caster's latitude/longitude (AAP §0.6.5).
                double.TryParse(data[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double cLat);
                double.TryParse(data[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double cLon);

                double temp;
                if (cLat == 0 || cLon == 0)
                {
                    // No usable coordinates: use the original sentinel so the row sorts to the end.
                    temp = UnknownDistanceKm;
                }
                else
                {
                    // GetDistance returns metres; convert to kilometres exactly as the original.
                    temp = GetDistance(cLon, cLat, _lon, _lat);
                    temp *= .001;
                }

                string mount = data[0].Trim();
                string latText = data[1].Trim();
                string lonText = data[2].Trim();
                string format = data[3].Trim();
                string network = data[4].Trim();

                SourceEntry entry = new SourceEntry
                {
                    Mount = mount,
                    Latitude = latText,
                    Longitude = lonText,
                    Format = format,
                    Network = network,
                    Info = string.Join("   ", latText, lonText, format, network),
                    // [XPLAT] Distance display uses the original "#######" mask but with explicit
                    // InvariantCulture; the mask has no separators so the rendered text is identical.
                    DistanceText = temp.ToString("#######", CultureInfo.InvariantCulture).PadLeft(10),
                    DistanceKm = temp
                };

                Entries.Add(entry);
            }
        }

        /// <summary>
        /// Re-orders <see cref="Entries"/> in place according to <see cref="IsSortedByDistance"/>:
        /// ascending by <see cref="SourceEntry.DistanceKm"/> ("nearest first"), or ordinal by
        /// mountpoint name. An ordinal (culture-invariant) name comparison is used so the ordering
        /// is identical on every operating system in the CI matrix (AAP §0.6.5).
        /// </summary>
        private void ApplySort()
        {
            // Snapshot, sort, then refill the observable collection in place so existing bindings
            // (and the current selection, which references the same instances) are preserved.
            List<SourceEntry> sorted = new List<SourceEntry>(Entries);

            if (_isSortedByDistance)
            {
                sorted.Sort((a, b) => a.DistanceKm.CompareTo(b.DistanceKm));
            }
            else
            {
                sorted.Sort((a, b) => string.Compare(a.Mount, b.Mount, StringComparison.OrdinalIgnoreCase));
            }

            Entries.Clear();
            foreach (SourceEntry entry in sorted)
            {
                Entries.Add(entry);
            }
        }

        /// <summary>
        /// Confirms the operator's choice (the former <c>btnUseMount_Click</c>). Raises
        /// <see cref="RequestClose"/> with the selected mountpoint, or <see cref="string.Empty"/>
        /// when nothing is selected, so the host can close the dialog with the result.
        /// </summary>
        private void OnOk()
        {
            RequestClose(_selectedEntry != null ? _selectedEntry.Mount : string.Empty);
        }

        /// <summary>
        /// Dismisses the dialog without a selection (the former Cancel button). Raises
        /// <see cref="RequestClose"/> with <see cref="string.Empty"/>.
        /// </summary>
        private void OnCancel()
        {
            RequestClose(string.Empty);
        }

        /// <summary>
        /// Toggles the list ordering and re-applies the sort (the former <c>btnSort_Click</c>).
        /// </summary>
        private void OnSort()
        {
            IsSortedByDistance = !IsSortedByDistance;
            ApplySort();
        }

        /// <summary>
        /// Opens the caster's site/home URL in the operating system's default handler (the former
        /// <c>btnSite_Click</c>).
        /// </summary>
        private void OnVisitSite()
        {
            if (string.IsNullOrWhiteSpace(_site))
            {
                return;
            }

            // [XPLAT] Replaces the Windows-only Process.Start(site) shell behaviour with the
            // documented cross-platform pattern for modern .NET: a ProcessStartInfo with
            // UseShellExecute = true delegates to the OS default URL handler (Windows/macOS/Linux).
            // Kept best-effort: any failure (no handler, malformed URL) degrades to a silent no-op.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _site,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // Intentional graceful no-op: launching an external browser is a convenience, and a
                // headless or handler-less environment must never crash the picker.
            }
        }

        /// <summary>
        /// Computes the great-circle distance, in metres, between two points using the haversine
        /// formula. Ported verbatim from the WinForms original: identical trigonometry and Earth
        /// radius (6 376 500 m), so results are bit-for-bit equivalent (behaviour frozen).
        /// </summary>
        /// <param name="longitude">Longitude of the first point, in decimal degrees.</param>
        /// <param name="latitude">Latitude of the first point, in decimal degrees.</param>
        /// <param name="otherLongitude">Longitude of the second point, in decimal degrees.</param>
        /// <param name="otherLatitude">Latitude of the second point, in decimal degrees.</param>
        /// <returns>The great-circle distance between the two points, in metres.</returns>
        public double GetDistance(double longitude, double latitude, double otherLongitude, double otherLatitude)
        {
            double d1 = latitude * (Math.PI / 180.0);
            double num1 = longitude * (Math.PI / 180.0);
            double d2 = otherLatitude * (Math.PI / 180.0);
            double num2 = otherLongitude * (Math.PI / 180.0) - num1;
            double d3 = Math.Pow(Math.Sin((d2 - d1) / 2.0), 2.0) + Math.Cos(d1)
                * Math.Cos(d2) * Math.Pow(Math.Sin(num2 / 2.0), 2.0);

            return 6376500.0 * (2.0 * Math.Atan2(Math.Sqrt(d3), Math.Sqrt(1.0 - d3)));
        }
    }
}
