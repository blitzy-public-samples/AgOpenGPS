// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgOpenGPS.Core;
using AgOpenGPS.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for <c>FormCopyTracksView.axaml</c> — a 1:1 behavioural-parity reimplementation
    /// of the WinForms <c>Forms/Field/FormCopyTracks</c> (FormCopyTracks.cs + FormCopyTracks.Designer.cs),
    /// the "Import Tracks From Another Field" dialog. The operator picks a SOURCE field on the left (only
    /// fields that have both <c>Field.txt</c> and <c>TrackLines.txt</c>, excluding the currently-open
    /// field), the dialog lists that field's tracks on the right, the operator multi-selects one or more
    /// tracks and imports (copies) them — with per-field coordinate re-projection — into the current
    /// field.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Imperative dialog (NO <c>x:DataType</c> / <c>DataContext</c> / MVVM bindings): every control is
    /// addressed by its <c>x:Name</c> and the handlers keep their original WinForms names, exactly like
    /// the sibling code-behind dialogs. The two lists are data-driven via <see cref="ObservableCollection{T}"/>
    /// set as <c>ItemsSource</c> (the established pattern across the migrated Views); each row's
    /// <c>ToString()</c> supplies its displayed text because the ListBoxes declare no <c>ItemTemplate</c>.
    /// </para>
    /// <para>
    /// [XPLAT] WinForms host coupling is replaced by dependency injection (AAP §0.3.2): the WinForms form
    /// reached the FormGPS god-object via <c>mf.currentFieldDirectory</c>, <c>mf.AppModel</c>,
    /// <c>mf.FileSaveTracks()</c>, <c>mf.FileLoadTracks()</c> and the live <c>mf.trk</c> track manager.
    /// Here the host sets <see cref="currentFieldDirectory"/> / <see cref="AppModel"/> and wires the
    /// <see cref="FileSaveTracks"/> / <see cref="FileLoadTracks"/> callbacks before the dialog is shown.
    /// The actual track copy is performed in-place by the portable, behaviour-frozen
    /// <c>TrackCopier.CopyTracksToField</c> (no host round-trip). The only operation that is delegated
    /// back to the host is the post-copy in-memory reload of the live track manager — see the remarks on
    /// <see cref="FileLoadTracks"/>.
    /// </para>
    /// <para>
    /// XPLAT conversions from the WinForms original: the <c>ListView</c>/<c>ListViewItem</c> pair becomes
    /// <c>ListBox</c> + <see cref="ObservableCollection{T}"/>; the per-row <c>BackColor</c>/<c>ForeColor</c>
    /// painting (orange/white selected field, green/white selected tracks) becomes the ListBox's native
    /// multi-select highlight styled in the <c>.axaml</c>; the <c>ImageList</c> row-height hack is dropped
    /// (the <c>.axaml</c> sets a touch-friendly min row height); <c>Application.DoEvents()</c> is removed
    /// (status text repaints on the next layout pass); and <c>FormDialog.Show</c> becomes
    /// <c>await FormDialogView.ShowAsync(...)</c>. No WinForms / System.Drawing / OpenTK / GMap types are
    /// referenced. See MIGRATION_DOCS/TRANSITION_MAP.md.
    /// </para>
    /// </remarks>
    public partial class FormCopyTracksView : Window
    {
        // [XPLAT] The source-field list and the selected field's tracks. WinForms mutated ListViewItems in
        // place; here the two ListBoxes are bound to these collections (set as ItemsSource in the
        // constructor). FieldItem.ToString() renders the field name; TrackItem.ToString() renders the
        // track display label.
        private readonly ObservableCollection<FieldItem> fieldItems = new ObservableCollection<FieldItem>();
        private readonly ObservableCollection<TrackItem> trackItems = new ObservableCollection<TrackItem>();

        // [XPLAT] Absolute path of the SOURCE field directory the operator selected on the left, or null.
        // Mirrors the WinForms private field of the same name.
        private string selectedFieldDirectory;

        // [XPLAT] One-shot guard so the source-field list is populated exactly once — parity with the
        // WinForms Load event (which fired once). Avalonia's OnLoaded can fire again if the window is
        // detached and re-attached to the visual tree; this prevents a redundant reload.
        private bool fieldListLoaded;

        /// <summary>
        /// [XPLAT] Name of the currently-open field's directory — the field tracks are imported INTO.
        /// Replaces the WinForms <c>mf.currentFieldDirectory</c> back-reference (AAP §0.3.2): the host
        /// assigns it before showing the dialog. It is used both to exclude the current field from the
        /// source list and to build the import target path. A null/empty value means no field is open,
        /// which blocks the import exactly as the original did.
        /// </summary>
        public string currentFieldDirectory { get; set; }

        /// <summary>
        /// [XPLAT] The shared application model (replaces <c>mf.AppModel</c>). Its
        /// <see cref="ApplicationModel.SharedFieldProperties"/> drives the per-field coordinate
        /// re-projection performed by <c>TrackCopier.CopyTracksToField</c>.
        /// </summary>
        public ApplicationModel AppModel { get; set; }

        /// <summary>
        /// [XPLAT] Host callback that flushes the current field's in-memory tracks to disk before the
        /// import (replaces <c>mf.FileSaveTracks()</c>). Injected as an <see cref="Action"/> so the view
        /// stays free of the FormGPS god-object.
        /// </summary>
        public Action FileSaveTracks { get; set; }

        /// <summary>
        /// [XPLAT] Host callback that reloads the current field's tracks into the live track manager after
        /// the import and re-selects the first visible track (replaces the WinForms
        /// <c>mf.trk.gArr?.Clear(); mf.FileLoadTracks();</c> followed by the first-visible index scan that
        /// set <c>mf.trk.idx</c>). This is host-owned because it mutates the <c>CTrack</c> track manager,
        /// which is FormGPS-coupled and therefore intentionally NOT referenced by any Avalonia View at
        /// this checkpoint (it is gated out of compilation in <c>AgOpenGPS.csproj</c> and decoupled into a
        /// service later, per AAP §0.6.1). The view triggers it through this injected <see cref="Action"/>
        /// once the on-disk copy succeeds, preserving the original observable behaviour.
        /// </summary>
        public Action FileLoadTracks { get; set; }

        /// <summary>
        /// [XPLAT] Initialises the dialog. The <c>.axaml</c> sets the window title ("Import Tracks"); this
        /// binds the two collections as the list item sources. The source-field list itself is populated
        /// in <see cref="OnLoaded"/> (the WinForms <c>FormCopyTracks_Load</c> equivalent), once the
        /// injected <see cref="currentFieldDirectory"/> is available.
        /// </summary>
        public FormCopyTracksView()
        {
            InitializeComponent();

            lbFields.ItemsSource = fieldItems;
            lvTracks.ItemsSource = trackItems;
        }

        // [XPLAT] FormCopyTracks_Load -> OnLoaded: populate the source-field list once the window is
        // loaded. Fire-and-forget (explicit discard) because LoadFieldList owns its try/catch and never
        // throws; the one-shot guard keeps it to a single run, matching the WinForms Load semantics.
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            if (fieldListLoaded)
            {
                return;
            }

            fieldListLoaded = true;
            _ = LoadFieldList();
        }

        /// <summary>
        /// [XPLAT] Port of <c>LoadFieldList</c>: list every field directory under
        /// <c>RegistrySettings.fieldsDirectory</c> that has BOTH a <c>Field.txt</c> and a
        /// <c>TrackLines.txt</c> and is not the currently-open field, then select the first one. Sets the
        /// status line to the original "Found N field(s) with tracks" (or "Fields directory not found").
        /// </summary>
        private async Task LoadFieldList()
        {
            try
            {
                string fieldsDir = RegistrySettings.fieldsDirectory;
                if (string.IsNullOrEmpty(fieldsDir) || !Directory.Exists(fieldsDir))
                {
                    lblStatus.Text = "Fields directory not found";
                    return;
                }

                fieldItems.Clear();

                foreach (string fieldDir in Directory.GetDirectories(fieldsDir))
                {
                    // Only fields that have both a Field.txt and a TrackLines.txt can supply tracks.
                    string fieldFile = Path.Combine(fieldDir, "Field.txt");
                    string trackFile = Path.Combine(fieldDir, "TrackLines.txt");
                    if (!File.Exists(fieldFile) || !File.Exists(trackFile))
                    {
                        continue;
                    }

                    // Never offer the currently-open field as an import source.
                    string fieldName = Path.GetFileName(fieldDir);
                    if (!string.IsNullOrEmpty(currentFieldDirectory) && fieldName == currentFieldDirectory)
                    {
                        continue;
                    }

                    fieldItems.Add(new FieldItem
                    {
                        Name = fieldName,
                        FullPath = fieldDir
                    });
                }

                lblStatus.Text = $"Found {fieldItems.Count} field(s) with tracks";

                // Selecting the first field raises lbFields_SelectedIndexChanged, which loads its tracks
                // (and overwrites the status line) — exactly as the WinForms list did.
                if (fieldItems.Count > 0)
                {
                    lbFields.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                await FormDialogView.ShowAsync("Import Tracks", "Failed to load field list: " + ex.Message, DialogSeverity.Error, this);
                lblStatus.Text = "Error loading fields";
            }
        }

        // [XPLAT] lbFields_SelectedIndexChanged: when the operator picks a source field, remember its path
        // and load its tracks. The WinForms orange/white row recolour is now the ListBox's native
        // selection highlight (styled in the .axaml), so no per-row colour code remains here.
        private async void lbFields_SelectedIndexChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lbFields.SelectedItem is not FieldItem selected)
            {
                return;
            }

            selectedFieldDirectory = selected.FullPath;
            await LoadTracksFromField(selected.FullPath);
        }

        /// <summary>
        /// [XPLAT] Port of <c>LoadTracksFromField</c>: load the chosen field's tracks via the portable
        /// <c>TrackFiles.Load</c> and list each as "&lt;name&gt; (AB Line|Curve|&lt;mode&gt;)". The
        /// WinForms green/white per-row toggle is replaced by the multi-select ListBox.
        /// </summary>
        /// <param name="fieldDirectory">Absolute path of the source field directory to read tracks from.</param>
        private async Task LoadTracksFromField(string fieldDirectory)
        {
            try
            {
                List<CTrk> availableTracks = TrackFiles.Load(fieldDirectory);
                trackItems.Clear();

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

                    trackItems.Add(new TrackItem
                    {
                        Display = $"{trackName} ({trackType})",
                        Track = track
                    });
                }

                lblStatus.Text = $"{trackItems.Count} track(s) available for importing";
            }
            catch (Exception ex)
            {
                await FormDialogView.ShowAsync("Import Tracks", "Failed to load tracks: " + ex.Message, DialogSeverity.Error, this);
                lblStatus.Text = "Error loading tracks";
            }
        }

        // [XPLAT] btnSelectAllTracks_Click: select every track in the multi-select list (replaces the
        // WinForms loop that painted every row green).
        private void btnSelectAllTracks_Click(object sender, RoutedEventArgs e)
        {
            lvTracks.SelectAll();
        }

        // [XPLAT] btnDeselectAllTracks_Click: clear the track selection (replaces the WinForms loop that
        // reset every row to white).
        private void btnDeselectAllTracks_Click(object sender, RoutedEventArgs e)
        {
            lvTracks.UnselectAll();
        }

        /// <summary>
        /// [XPLAT] Port of <c>btnCopyToCurrentField_Click</c> — the behaviour-frozen import. Validates that
        /// a source field is selected, that at least one track is chosen, and that a field is open; flushes
        /// the current field's tracks (<see cref="FileSaveTracks"/>); copies the selected tracks into the
        /// current field with per-field coordinate re-projection via the portable
        /// <c>TrackCopier.CopyTracksToField</c>; reloads the current field's tracks
        /// (<see cref="FileLoadTracks"/>); and reports the result through <c>FormDialogView</c>.
        /// </summary>
        private async void btnCopyToCurrentField_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // A source field must be selected.
                if (string.IsNullOrEmpty(selectedFieldDirectory))
                {
                    await FormDialogView.ShowAsync("Import Tracks", "Please select a field first.", DialogSeverity.Error, this);
                    return;
                }

                // Gather the multi-selected tracks.
                List<CTrk> selectedTracks = lvTracks.SelectedItems.Cast<TrackItem>().Select(t => t.Track).ToList();
                if (selectedTracks.Count == 0)
                {
                    await FormDialogView.ShowAsync("Import Tracks", "Please select at least one track to import.", DialogSeverity.Error, this);
                    return;
                }

                // A field must be open to import into.
                if (string.IsNullOrEmpty(currentFieldDirectory))
                {
                    await FormDialogView.ShowAsync("Import Tracks", "No field is currently open.", DialogSeverity.Error, this);
                    return;
                }

                // Build the absolute path of the current (target) field directory.
                string currentFieldFullPath = Path.Combine(RegistrySettings.fieldsDirectory, currentFieldDirectory);

                // First flush any changes to the current field's tracks (host-owned save).
                lblStatus.Text = "Saving current tracks...";
                FileSaveTracks?.Invoke();

                // Convert + copy the selected tracks into the current field (portable, behaviour frozen).
                lblStatus.Text = "Converting tracks...";
                int copiedCount = TrackCopier.CopyTracksToField(
                    selectedFieldDirectory,
                    currentFieldFullPath,
                    selectedTracks,
                    AppModel.SharedFieldProperties);

                // Reload the current field's tracks and re-select the first visible one. This mutates the
                // FormGPS-coupled track manager, so it is delegated to the host — see FileLoadTracks.
                lblStatus.Text = "Saving tracks...";
                FileLoadTracks?.Invoke();

                lblStatus.Text = $"Successfully imported {copiedCount} track(s)";
                await FormDialogView.ShowAsync("Import Tracks", $"Successfully imported {copiedCount} track(s) to current field.", DialogSeverity.Info, this);
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error: " + ex.Message;
                await FormDialogView.ShowAsync("Import Tracks", "Error importing tracks: " + ex.Message, DialogSeverity.Error, this);
            }
        }

        // [XPLAT] btnClose_Click: close the dialog.
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // [XPLAT] Row models for the two ListBoxes. ToString() supplies the displayed text because the
        // ListBoxes declare no ItemTemplate (parity with the WinForms ListViewItem text).

        /// <summary>A selectable source field: its display name and absolute directory path.</summary>
        private sealed class FieldItem
        {
            public string Name { get; set; }

            public string FullPath { get; set; }

            public override string ToString() => Name;
        }

        /// <summary>A selectable track: its display label and the underlying track model.</summary>
        private sealed class TrackItem
        {
            public string Display { get; set; }

            public CTrk Track { get; set; }

            public override string ToString() => Display;
        }
    }
}
