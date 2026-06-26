// [XPLAT] migrated from net48/WinForms (Forms/Field/FormCopyTracks.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using AgOpenGPS.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the "Import / Copy Tracks From Another Field" dialog — a faithful port
    /// of the WinForms <c>FormCopyTracks</c> (FormCopyTracks.cs + FormCopyTracks.Designer.cs). The
    /// operator picks a SOURCE field on the left (only fields that have both Field.txt and
    /// TrackLines.txt), the dialog lists that field's tracks on the right, the operator multi-selects
    /// one or more and imports them into the currently-open field.
    /// </summary>
    /// <remarks>
    /// Imperative dialog (NO DataContext / x:DataType / MVVM bindings). Handler names are kept from the
    /// original. Self-contained behaviour is ported in full:
    /// <list type="bullet">
    ///   <item><c>LoadFieldList</c> (run from <see cref="OnOpened"/>) enumerates
    ///         <c>RegistrySettings.fieldsDirectory</c> and lists every field that has both Field.txt
    ///         and TrackLines.txt — directly satisfying the "runtime list population" requirement.</item>
    ///   <item><see cref="lbFields_SelectedIndexChanged"/> loads the selected field's tracks via the
    ///         portable <c>TrackFiles.Load</c> and renders each as "&lt;name&gt; (AB Line|Curve|…)".</item>
    ///   <item><see cref="btnSelectAllTracks_Click"/> / <see cref="btnDeselectAllTracks_Click"/> drive
    ///         the multi-select list (replacing the WinForms per-row colour toggle).</item>
    ///   <item><see cref="btnClose_Click"/> closes the dialog.</item>
    /// </list>
    /// The original excluded the currently-open field from the source list and performed the actual
    /// copy with <c>TrackCopier.CopyTracksToField(…, mf.AppModel.SharedFieldProperties)</c> followed by
    /// <c>mf.FileSaveTracks()</c> / <c>mf.FileLoadTracks()</c>. The coordinate re-projection between
    /// field origins and the reload depend on the FormGPS shared field properties, which are not
    /// projected at this checkpoint; <see cref="btnCopyToCurrentField_Click"/> validates the selection,
    /// publishes it via <see cref="SelectedFieldDirectory"/> / <see cref="SelectedTracks"/> and raises
    /// <see cref="CopyRequested"/> for the host to execute the copy — see MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </remarks>
    public partial class FormCopyTracksView : Window
    {
        private string _selectedFieldDirectory;
        private readonly List<CTrk> _selectedTracks = new List<CTrk>();

        /// <summary>Absolute path of the SOURCE field directory the operator selected, or <c>null</c>.</summary>
        public string SelectedFieldDirectory => _selectedFieldDirectory;

        /// <summary>The tracks the operator chose to import (populated when copy is requested).</summary>
        public IReadOnlyList<CTrk> SelectedTracks => _selectedTracks;

        /// <summary>
        /// Raised when the operator presses "Copy to current field" with a valid selection. The host
        /// reads <see cref="SelectedFieldDirectory"/> / <see cref="SelectedTracks"/>, converts the
        /// tracks between field origins and writes them into the current field.
        /// </summary>
        public event EventHandler CopyRequested;

        public FormCopyTracksView()
        {
            InitializeComponent();
        }

        // [XPLAT] FormCopyTracks_Load: populate the source-field list once the window is shown.
        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            LoadFieldList();
        }

        // [XPLAT] Enumerate fields that have both Field.txt and TrackLines.txt (self-contained IO).
        private void LoadFieldList()
        {
            try
            {
                string fieldsDir = RegistrySettings.fieldsDirectory;
                if (string.IsNullOrEmpty(fieldsDir) || !Directory.Exists(fieldsDir))
                {
                    lblStatus.Text = "Fields directory not found";
                    return;
                }

                lbFields.Items.Clear();
                foreach (string fieldDir in Directory.GetDirectories(fieldsDir))
                {
                    string fieldFile = Path.Combine(fieldDir, "Field.txt");
                    string trackFile = Path.Combine(fieldDir, "TrackLines.txt");
                    if (!File.Exists(fieldFile) || !File.Exists(trackFile))
                    {
                        continue;
                    }

                    // [XPLAT] The WinForms list also excluded mf.currentFieldDirectory; that exclusion
                    // is host-owned because the current field lives on the FormGPS god-object.
                    lbFields.Items.Add(new FieldEntry
                    {
                        Name = Path.GetFileName(fieldDir),
                        FullPath = fieldDir
                    });
                }

                lblStatus.Text = $"Found {lbFields.Items.Count} field(s) with tracks";
                if (lbFields.Items.Count > 0)
                {
                    lbFields.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error loading fields: " + ex.Message;
            }
        }

        // [XPLAT] lbFields_SelectedIndexChanged: load the chosen field's tracks into lvTracks.
        private void lbFields_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!(lbFields.SelectedItem is FieldEntry entry))
            {
                return;
            }

            _selectedFieldDirectory = entry.FullPath;
            LoadTracksFromField(entry.FullPath);
        }

        // [XPLAT] LoadTracksFromField: TrackFiles.Load is cross-platform; map mode to a display label.
        private void LoadTracksFromField(string fieldDirectory)
        {
            try
            {
                List<CTrk> availableTracks = TrackFiles.Load(fieldDirectory);
                lvTracks.Items.Clear();

                if (availableTracks.Count == 0)
                {
                    lblStatus.Text = "No tracks found in this field";
                    return;
                }

                foreach (CTrk track in availableTracks)
                {
                    string trackName = track.name ?? "Unnamed Track";
                    string trackType = track.mode == TrackMode.AB ? "AB Line" :
                                       track.mode == TrackMode.Curve ? "Curve" :
                                       track.mode.ToString();
                    lvTracks.Items.Add(new TrackEntry
                    {
                        Track = track,
                        Display = $"{trackName} ({trackType})"
                    });
                }

                lblStatus.Text = $"{lvTracks.Items.Count} track(s) available for importing";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error loading tracks: " + ex.Message;
            }
        }

        // [XPLAT] btnSelectAllTracks_Click: select every track in the multi-select list.
        private void btnSelectAllTracks_Click(object sender, RoutedEventArgs e)
        {
            lvTracks.SelectAll();
        }

        // [XPLAT] btnDeselectAllTracks_Click: clear the track selection.
        private void btnDeselectAllTracks_Click(object sender, RoutedEventArgs e)
        {
            lvTracks.UnselectAll();
        }

        // [XPLAT] btnCopyToCurrentField_Click: validate, publish the selection and ask the host to copy.
        private void btnCopyToCurrentField_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFieldDirectory))
            {
                lblStatus.Text = "Please select a field first.";
                return;
            }

            _selectedTracks.Clear();
            foreach (object item in lvTracks.SelectedItems)
            {
                if (item is TrackEntry entry)
                {
                    _selectedTracks.Add(entry.Track);
                }
            }

            if (_selectedTracks.Count == 0)
            {
                lblStatus.Text = "Please select at least one track to import.";
                return;
            }

            lblStatus.Text = $"Importing {_selectedTracks.Count} track(s)…";
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }

        // [XPLAT] btnClose_Click: close the dialog.
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // [XPLAT] List item models. ToString() drives display (the ListBoxes declare no ItemTemplate).
        private sealed class FieldEntry
        {
            public string Name { get; set; }
            public string FullPath { get; set; }
            public override string ToString() => Name;
        }

        private sealed class TrackEntry
        {
            public CTrk Track { get; set; }
            public string Display { get; set; }
            public override string ToString() => Display;
        }
    }
}
