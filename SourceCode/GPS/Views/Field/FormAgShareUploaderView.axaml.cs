// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AgLibrary.Logging;
using AgOpenGPS.Core.AgShare;
using AgOpenGPS.Core.Models;
using AgOpenGPS.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Code-behind for the AgShare bulk-upload dialog — a 1:1 behavioural-parity reimplementation
    /// of the WinForms <c>FormAgShareUploader</c> (Forms/Field/FormAgShareUploader.cs +
    /// FormAgShareUploader.Designer.cs + FormAgShareUploader.resx). The dialog lists every local field as
    /// a checkbox in a scrolling panel, lets the operator Select-All / Deselect-All, then uploads the
    /// ticked fields to the AgShare cloud while a progress bar advances and a status line reports each
    /// field; already-uploaded fields are prefixed with a ☁ glyph.
    /// </summary>
    /// <remarks>
    /// This is an imperative, code-behind dialog (no DataContext, no <c>x:DataType</c>, no MVVM bindings):
    /// controls are addressed by their <c>x:Name</c> and the click handlers are attached by name in the
    /// constructor, exactly mirroring the WinForms partial that used direct member access. The AgShare
    /// upload domain logic (<see cref="CheckCloudStatusAsync"/>, <see cref="UploadField"/>,
    /// <see cref="FindFieldByNameOnCloud"/>, <see cref="LoadFieldSnapshot"/>) is BEHAVIOUR-FROZEN and
    /// ported verbatim from the WinForms source — the cross-platform changes are confined to the UI
    /// surface (WinForms controls → Avalonia controls, <c>FormDialog</c> → <see cref="FormDialogView"/>,
    /// removal of <c>Application.DoEvents()</c>). The constructor takes an injected
    /// <see cref="AgShareClient"/> — there is no <c>FormGPS</c> back-reference.
    /// </remarks>
    public partial class FormAgShareUploaderView : Window
    {
        // [XPLAT] AgShare upload collaborator + network client (behaviour-frozen domain objects). The
        // WinForms form held the identical pair; the client is injected and the uploader is created from it.
        private readonly AgShareUploader uploader;
        private readonly AgShareClient client;

        // Local fields discovered under the fields directory; rebuilt on every load.
        private List<FieldInfo> availableFields;

        // Re-entrancy / close guard while a bulk upload is running (parity with the WinForms field).
        private bool isUploading = false;

        // [XPLAT] WinForms used Color.FromArgb(0, 119, 190) ("OceanBlue") for the selected / on-cloud
        // highlight. The shared, immutable brush reproduces that exact colour for every checkbox.
        private static readonly IBrush OceanBlueBrush = new SolidColorBrush(Color.FromRgb(0, 119, 190));

        /// <summary>
        /// [XPLAT] Parameterless constructor required by the Avalonia compiled-XAML loader (rule AVLN3001).
        /// It is not used by the application — every real instance is created through the
        /// <see cref="FormAgShareUploaderView(AgShareClient)"/> overload — but must exist for the generated
        /// <c>InitializeComponent()</c> partner to compile.
        /// </summary>
        public FormAgShareUploaderView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// [XPLAT] Primary constructor — mirrors the WinForms ctor <c>FormAgShareUploader(AgShareClient)</c>.
        /// Stores the injected client, builds the <see cref="AgShareUploader"/> from it, and wires the
        /// button click handlers by name (the markup intentionally declares none).
        /// </summary>
        /// <param name="agShareClient">The AgShare network client used for cloud queries and uploads.</param>
        public FormAgShareUploaderView(AgShareClient agShareClient)
            : this()
        {
            client = agShareClient;
            uploader = new AgShareUploader(agShareClient);

            // [XPLAT] Markup wires no handlers (the established AgShare-sibling convention); attach by name.
            btnSelectAll.Click += btnSelectAll_Click;
            btnDeselectAll.Click += btnDeselectAll_Click;
            btnUpload.Click += btnUpload_Click;
            btnClose.Click += btnClose_Click;
        }

        /// <summary>
        /// [XPLAT] Ports <c>FormAgShareBulkUploader_Load</c>: populate the field list, then kick off the
        /// fire-and-forget cloud-status check exactly as the WinForms Load handler did.
        /// </summary>
        /// <param name="e">The routed-event payload.</param>
        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);

            LoadAvailableFields();

            // Check cloud status async after loading (fire-and-forget, verbatim with the source).
            _ = CheckCloudStatusAsync();
        }

        /// <summary>
        /// Enumerates the fields directory and builds one selectable checkbox per field that contains a
        /// <c>Field.txt</c>. Parity with the WinForms <c>LoadAvailableFields</c>.
        /// </summary>
        private void LoadAvailableFields()
        {
            availableFields = new List<FieldInfo>();
            flpFieldList.Children.Clear();

            try
            {
                string fieldsDirectory = RegistrySettings.fieldsDirectory;
                if (!Directory.Exists(fieldsDirectory))
                {
                    lblStatus.Text = "Fields directory not found";
                    return;
                }

                // Get all subdirectories (each is a field)
                string[] fieldDirectories = Directory.GetDirectories(fieldsDirectory);

                foreach (string fieldDir in fieldDirectories)
                {
                    string fieldName = Path.GetFileName(fieldDir);

                    // Check if field has necessary files (Field.txt contains origin)
                    string fieldFile = Path.Combine(fieldDir, "Field.txt");
                    if (File.Exists(fieldFile))
                    {
                        FieldInfo fieldInfo = new FieldInfo
                        {
                            Name = fieldName,
                            DirectoryPath = fieldDir,
                            IsOnCloud = false,  // Will be determined by cloud check
                            CloudFieldId = null
                        };

                        availableFields.Add(fieldInfo);

                        // Create checkbox for this field
                        CheckBox checkbox = CreateFieldCheckbox(fieldInfo);
                        flpFieldList.Children.Add(checkbox);
                    }
                }

                lblStatus.Text = $"Found {availableFields.Count} fields";
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Error loading fields: " + ex.Message;
                Log.EventWriter("Error loading fields for bulk upload: " + ex.Message);
            }
        }

        /// <summary>
        /// [XPLAT] Builds the per-field <see cref="CheckBox"/>. WinForms <c>Text</c>/<c>Checked</c>/
        /// <c>CheckedChanged</c> become Avalonia <c>Content</c>/<c>IsChecked</c>/<c>IsCheckedChanged</c>;
        /// the WinForms fixed <c>Width = panel.Width - 25</c> becomes a stretched horizontal alignment so
        /// the row fills the (now resizable) list. Tahoma 20pt → 26.67 device-independent pixels (×96/72).
        /// </summary>
        /// <param name="fieldInfo">The field this checkbox represents (stored in <c>Tag</c>).</param>
        /// <returns>The configured checkbox.</returns>
        private CheckBox CreateFieldCheckbox(FieldInfo fieldInfo)
        {
            CheckBox checkbox = new CheckBox
            {
                Content = fieldInfo.Name,  // Cloud check will add ☁ later if needed
                IsChecked = false,
                Height = 45,
                Tag = fieldInfo,
                FontFamily = new FontFamily("Tahoma"),
                FontSize = 26.67,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 8, 0, 0),
                Background = Brushes.Transparent,
                Foreground = Brushes.Black
            };

            checkbox.IsCheckedChanged += OnFieldSelectionChanged;
            return checkbox;
        }

        /// <summary>
        /// [XPLAT] Recolours a row on selection — checked rows show the OceanBlue highlight with white text,
        /// unchecked rows revert to transparent with black text. Parity with <c>OnFieldSelectionChanged</c>.
        /// </summary>
        private void OnFieldSelectionChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkbox && checkbox.Tag is FieldInfo)
            {
                if (checkbox.IsChecked == true)
                {
                    checkbox.Background = OceanBlueBrush; // OceanBlue
                    checkbox.Foreground = Brushes.White;
                }
                else
                {
                    checkbox.Background = Brushes.Transparent;
                    checkbox.Foreground = Brushes.Black;
                }
            }
        }

        /// <summary>
        /// [XPLAT] BEHAVIOUR-FROZEN. Queries the operator's AgShare account and marks every local field that
        /// also exists on the cloud (by <c>agshare.txt</c> id first, then by name), prefixing matched rows
        /// with the ☁ glyph. The server call is awaited off the UI marshal; all control mutations run inside
        /// <see cref="Dispatcher"/>.<c>UIThread.Invoke</c> (the faithful equivalent of the WinForms
        /// <c>this.Invoke</c>). The whole pass fails silently — the cloud check is optional.
        /// </summary>
        private async Task CheckCloudStatusAsync()
        {
            try
            {
                var result = await client.GetOwnFieldsAsync();
                if (!result.IsSuccessful || result.Data == null)
                {
                    return; // Silently fail if cloud check fails
                }

                var cloudFields = result.Data;

                // Update UI on UI thread
                Dispatcher.UIThread.Invoke(() =>
                {
                    foreach (var child in flpFieldList.Children)
                    {
                        if (child is CheckBox checkbox && checkbox.Tag is FieldInfo fieldInfo)
                        {
                            fieldInfo.IsOnCloud = false;
                            fieldInfo.CloudFieldId = null;

                            // First check if local agshare.txt exists and has a valid ID
                            string idPath = Path.Combine(fieldInfo.DirectoryPath, "agshare.txt");
                            if (File.Exists(idPath))
                            {
                                try
                                {
                                    string raw = File.ReadAllText(idPath).Trim();
                                    Guid localId = Guid.Parse(raw);

                                    // Check if this ID exists in current user's cloud fields
                                    var cloudField = cloudFields.FirstOrDefault(f => f.Id == localId);
                                    if (cloudField != null)
                                    {
                                        // ID belongs to current user - mark as on cloud
                                        fieldInfo.IsOnCloud = true;
                                        fieldInfo.CloudFieldId = localId;
                                    }
                                    else
                                    {
                                        // ID in agshare.txt but NOT in current user's cloud fields
                                        // Could be different user OR same user with different/outdated ID
                                        // Will check by name next
                                        Log.EventWriter($"AgShare: Field '{fieldInfo.Name}' has agshare.txt ID not found in cloud fields. Checking by name...");
                                    }
                                }
                                catch
                                {
                                    // Invalid agshare.txt, ignore - will check by name below
                                }
                            }

                            // Only check by name if we haven't found the field via ID yet
                            // (even if agshare.txt exists, the ID might be wrong/outdated)
                            if (!fieldInfo.IsOnCloud)
                            {
                                var cloudField = cloudFields.FirstOrDefault(f => f.Name == fieldInfo.Name);
                                if (cloudField != null)
                                {
                                    // Found field with same name on cloud - mark as on cloud
                                    // This handles the case where local agshare.txt has a different ID
                                    fieldInfo.IsOnCloud = true;
                                    fieldInfo.CloudFieldId = cloudField.Id;
                                }
                            }

                            // Update checkbox text based on cloud status
                            if (fieldInfo.IsOnCloud)
                            {
                                checkbox.Content = $"☁ {fieldInfo.Name}";
                                checkbox.Foreground = OceanBlueBrush;
                            }
                            else
                            {
                                checkbox.Content = fieldInfo.Name;
                                checkbox.Foreground = Brushes.Black;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                // Silently fail - cloud check is optional
                Log.EventWriter($"AgShare cloud status check failed: {ex.Message}");
            }
        }

        /// <summary>Ticks every field checkbox. Parity with <c>btnSelectAll_Click</c>.</summary>
        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in flpFieldList.Children)
            {
                if (child is CheckBox checkbox)
                {
                    checkbox.IsChecked = true;
                }
            }
        }

        /// <summary>Clears every field checkbox. Parity with <c>btnDeselectAll_Click</c>.</summary>
        private void btnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var child in flpFieldList.Children)
            {
                if (child is CheckBox checkbox)
                {
                    checkbox.IsChecked = false;
                }
            }
        }

        /// <summary>
        /// [XPLAT] Validates the selection, confirms with the operator, then runs the bulk upload. Parity
        /// with <c>btnUpload_Click</c>: <c>FormDialog.Show</c> → awaited <see cref="FormDialogView.ShowAsync"/>;
        /// <c>FormDialog.ShowQuestion(...) != DialogResult.OK</c> → <see cref="FormDialogView.ShowQuestionBlocking"/>.
        /// </summary>
        private async void btnUpload_Click(object sender, RoutedEventArgs e)
        {
            if (isUploading)
            {
                await FormDialogView.ShowAsync("Please Wait", "Upload already in progress", DialogSeverity.Info);
                return;
            }

            // Get selected fields
            List<FieldInfo> selectedFields = new List<FieldInfo>();
            foreach (var child in flpFieldList.Children)
            {
                if (child is CheckBox checkbox && checkbox.IsChecked == true && checkbox.Tag is FieldInfo fieldInfo)
                {
                    selectedFields.Add(fieldInfo);
                }
            }

            if (selectedFields.Count == 0)
            {
                await FormDialogView.ShowAsync("No Selection", "Please select at least one field to upload", DialogSeverity.Info);
                return;
            }

            // Confirm upload
            if (!FormDialogView.ShowQuestionBlocking("Confirm Upload", $"Upload {selectedFields.Count} field(s) to AgShare?"))
            {
                return;
            }

            await PerformBulkUpload(selectedFields);
        }

        /// <summary>
        /// [XPLAT] BEHAVIOUR-FROZEN upload loop. Disables the command buttons, drives the progress bar and
        /// status line, uploads each field, accumulates success/fail counts (logging the detailed error for
        /// each failure), reports a summary dialog, and re-enables the buttons in <c>finally</c>. The WinForms
        /// <c>Application.DoEvents()</c> calls are removed — the loop already <c>await</c>s, and the retained
        /// <c>await Task.Delay(500)</c> yields to the UI between fields exactly as the original status pause did.
        /// </summary>
        /// <param name="selectedFields">The fields the operator chose to upload.</param>
        private async Task PerformBulkUpload(List<FieldInfo> selectedFields)
        {
            isUploading = true;
            btnUpload.IsEnabled = false;
            btnClose.IsEnabled = false;
            btnSelectAll.IsEnabled = false;
            btnDeselectAll.IsEnabled = false;

            progressBar.Maximum = selectedFields.Count;
            progressBar.Value = 0;

            int successCount = 0;
            int failCount = 0;

            try
            {
                foreach (var fieldInfo in selectedFields)
                {
                    lblStatus.Text = $"Uploading: {fieldInfo.Name}...";

                    try
                    {
                        await UploadField(fieldInfo);
                        successCount++;
                        lblStatus.Text = $"Uploaded: {fieldInfo.Name} ✓";
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        lblStatus.Text = $"Failed: {fieldInfo.Name} ✗";

                        // Log detailed error information
                        string errorDetails = $"AgShare Upload Failed - Field: {fieldInfo.Name}\n" +
                                            $"Error: {ex.Message}\n";

                        if (ex.InnerException != null)
                        {
                            errorDetails += $"Inner Error: {ex.InnerException.Message}\n";
                        }

                        errorDetails += $"StackTrace: {ex.StackTrace}";
                        Log.EventWriter(errorDetails);
                    }

                    progressBar.Value++;
                    await Task.Delay(500); // Small delay to show status (also yields to the UI)
                }

                // Build summary message
                string summary = $"Upload Complete\n\nSuccessful: {successCount}\nFailed: {failCount}";

                if (failCount > 0)
                {
                    summary += "\n\nCheck Log Viewer to view problems";
                }

                lblStatus.Text = $"Upload complete: {successCount} succeeded, {failCount} failed";
                await FormDialogView.ShowAsync("Upload Complete", summary, failCount > 0 ? DialogSeverity.Warning : DialogSeverity.Info);
            }
            finally
            {
                isUploading = false;
                btnUpload.IsEnabled = true;
                btnClose.IsEnabled = true;
                btnSelectAll.IsEnabled = true;
                btnDeselectAll.IsEnabled = true;
            }
        }

        /// <summary>
        /// [XPLAT] BEHAVIOUR-FROZEN. Loads the field snapshot and reconciles its AgShare id against the local
        /// <c>agshare.txt</c> and the cloud (prompting the operator on conflicts), then uploads it. Ported
        /// verbatim from the WinForms <c>UploadField</c>; the only change is the dialog plumbing inside the
        /// <c>Ask*</c> helpers. The control flow (including the original behaviour where a "cancel" choice in
        /// the cloud-id-mismatch branch is swallowed by the surrounding catch) is preserved exactly.
        /// </summary>
        /// <param name="fieldInfo">The field to upload.</param>
        private async Task UploadField(FieldInfo fieldInfo)
        {
            // Load field data from directory
            var snapshot = await LoadFieldSnapshot(fieldInfo);

            if (snapshot == null)
            {
                throw new Exception("Failed to load field data");
            }

            string idPath = Path.Combine(fieldInfo.DirectoryPath, "agshare.txt");

            // Scenario: Cloud ID was found via name check, but local agshare.txt has different ID
            if (fieldInfo.CloudFieldId.HasValue && File.Exists(idPath))
            {
                try
                {
                    string raw = File.ReadAllText(idPath).Trim();
                    Guid localId = Guid.Parse(raw);

                    // If cloud ID (from name match) is different from local ID, ask user
                    if (localId != fieldInfo.CloudFieldId.Value)
                    {
                        DuplicateNameChoice choice = AskDifferentCloudIdChoice(snapshot.FieldName);

                        if (choice == DuplicateNameChoice.Cancel)
                        {
                            throw new Exception("Upload cancelled by user");
                        }
                        else if (choice == DuplicateNameChoice.Overwrite)
                        {
                            // Overwrite: Use cloud ID (overwrite cloud field with this ID)
                            snapshot.FieldId = fieldInfo.CloudFieldId.Value;
                            File.WriteAllText(idPath, fieldInfo.CloudFieldId.Value.ToString());
                        }
                        // else: CreateNew - generate new ID (already done in LoadFieldSnapshot)
                    }
                    else
                    {
                        // IDs match - use cloud ID
                        snapshot.FieldId = fieldInfo.CloudFieldId.Value;
                    }
                }
                catch
                {
                    // Invalid agshare.txt, use cloud ID
                    snapshot.FieldId = fieldInfo.CloudFieldId.Value;
                    File.WriteAllText(idPath, fieldInfo.CloudFieldId.Value.ToString());
                }
            }
            // Scenario: Cloud ID found via name check, no local agshare.txt
            else if (fieldInfo.CloudFieldId.HasValue)
            {
                snapshot.FieldId = fieldInfo.CloudFieldId.Value;
                File.WriteAllText(idPath, fieldInfo.CloudFieldId.Value.ToString());
            }
            // Scenario: No cloud ID found during initial check, no local agshare.txt
            else if (!File.Exists(idPath))
            {
                // Check if field with same name exists on cloud (might have been added since initial check)
                var existingFieldId = await FindFieldByNameOnCloud(snapshot.FieldName);
                if (existingFieldId.HasValue)
                {
                    // Ask user what to do
                    DuplicateNameChoice choice = AskDuplicateNameChoice(snapshot.FieldName);

                    if (choice == DuplicateNameChoice.Cancel)
                    {
                        throw new Exception("Upload cancelled by user");
                    }
                    else if (choice == DuplicateNameChoice.Overwrite)
                    {
                        // Use existing cloud ID and save it locally
                        snapshot.FieldId = existingFieldId.Value;
                        File.WriteAllText(idPath, existingFieldId.Value.ToString());
                    }
                    // else: CreateNew - keep the new GUID that was already created
                }
            }

            // Use existing upload logic
            await uploader.UploadAsync(snapshot, null);
        }

        /// <summary>
        /// [XPLAT] BEHAVIOUR-FROZEN. Returns the cloud id of a field whose name matches, or <c>null</c>.
        /// Ported verbatim from the WinForms <c>FindFieldByNameOnCloud</c>.
        /// </summary>
        private async Task<Guid?> FindFieldByNameOnCloud(string fieldName)
        {
            try
            {
                var result = await client.GetOwnFieldsAsync();
                if (result.IsSuccessful && result.Data != null)
                {
                    var existing = result.Data.FirstOrDefault(f => f.Name == fieldName);
                    if (existing != null)
                    {
                        return existing.Id;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.EventWriter($"Error checking for duplicate field name '{fieldName}': {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// [XPLAT] Parity port of <c>AskDuplicateNameChoice</c>. The WinForms <c>FormDialog.ShowQuestion</c>
        /// exposed only OK / Cancel (it had no "No" button), so the <c>DialogResult.No</c> → CreateNew branch
        /// was already unreachable. <see cref="FormDialogView.ShowQuestionBlocking"/> returns the same two
        /// reachable outcomes — <c>true</c> (OK) → <see cref="DuplicateNameChoice.Overwrite"/>, <c>false</c>
        /// (Cancel) → <see cref="DuplicateNameChoice.Cancel"/> — preserving the original behaviour verbatim.
        /// The message text (including its Yes/No/Cancel wording) is kept exactly as the source had it.
        /// </summary>
        private DuplicateNameChoice AskDuplicateNameChoice(string fieldName)
        {
            // This must be called on UI thread
            string message = $"Field '{fieldName}' already exists on AgShare cloud.\n\n" +
                            "What do you want to do?\n\n" +
                            "Yes = Overwrite existing cloud field\n" +
                            "No = Create as new field with different ID\n" +
                            "Cancel = Skip this field";

            bool overwrite = FormDialogView.ShowQuestionBlocking("Duplicate Field Name", message);

            return overwrite ? DuplicateNameChoice.Overwrite : DuplicateNameChoice.Cancel;
        }

        /// <summary>
        /// [XPLAT] Parity port of <c>AskDifferentCloudIdChoice</c>. Same OK/Cancel mapping rationale as
        /// <see cref="AskDuplicateNameChoice"/>; message text preserved verbatim.
        /// </summary>
        private DuplicateNameChoice AskDifferentCloudIdChoice(string fieldName)
        {
            // This must be called on UI thread
            string message = $"Field '{fieldName}' exists on cloud with a different ID.\n\n" +
                            "Your local field has an ID in agshare.txt, but the cloud has a different ID for a field with the same name.\n\n" +
                            "What do you want to do?\n\n" +
                            "Yes = Overwrite the cloud field (use cloud ID)\n" +
                            "No = Create as new field with a new ID\n" +
                            "Cancel = Skip this field";

            bool overwrite = FormDialogView.ShowQuestionBlocking("Different Cloud ID", message);

            return overwrite ? DuplicateNameChoice.Overwrite : DuplicateNameChoice.Cancel;
        }

        /// <summary>
        /// [XPLAT] BEHAVIOUR-FROZEN. Loads origin, boundaries, tracks and the (existing or new) AgShare id
        /// from the field directory off the UI thread, returning a populated <see cref="FieldSnapshot"/> or
        /// <c>null</c> on failure. Ported verbatim from the WinForms <c>LoadFieldSnapshot</c>.
        /// </summary>
        private async Task<FieldSnapshot> LoadFieldSnapshot(FieldInfo fieldInfo)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Use existing IO classes to load field data
                    // Load origin from Field.txt
                    Wgs84 origin = FieldPlaneFiles.LoadOrigin(fieldInfo.DirectoryPath);

                    // Load boundaries from Boundary.txt
                    List<CBoundaryList> boundaryList = BoundaryFiles.Load(fieldInfo.DirectoryPath);
                    List<List<vec3>> boundaries = new List<List<vec3>>();
                    foreach (var bnd in boundaryList)
                    {
                        if (bnd.fenceLine != null && bnd.fenceLine.Count > 0)
                        {
                            boundaries.Add(bnd.fenceLine);
                        }
                    }

                    // Allow upload without boundary - boundary is optional

                    // Load tracks from TrackLines.txt
                    List<CTrk> tracks = TrackFiles.Load(fieldInfo.DirectoryPath);

                    // Get or create field ID
                    Guid fieldId;
                    string idPath = Path.Combine(fieldInfo.DirectoryPath, "agshare.txt");
                    if (File.Exists(idPath))
                    {
                        string raw = File.ReadAllText(idPath).Trim();
                        fieldId = Guid.Parse(raw);
                    }
                    else
                    {
                        fieldId = Guid.NewGuid();
                    }

                    // Create LocalPlane with the field's own origin
                    LocalPlane plane = new LocalPlane(origin, new SharedFieldProperties());

                    return new FieldSnapshot
                    {
                        FieldName = fieldInfo.Name,
                        FieldDirectory = fieldInfo.DirectoryPath,
                        FieldId = fieldId,
                        OriginLat = origin.Latitude,
                        OriginLon = origin.Longitude,
                        Convergence = 0,
                        Boundaries = boundaries,
                        Tracks = tracks,
                        Converter = plane
                    };
                }
                catch (Exception ex)
                {
                    Log.EventWriter($"Error loading field snapshot for {fieldInfo.Name}: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>
        /// [XPLAT] Closes the dialog. Parity with <c>btnClose_Click</c>: while an upload is in progress the
        /// close is refused with an informational dialog (the command buttons are also disabled during upload,
        /// so this is a belt-and-braces guard exactly as in the WinForms source).
        /// </summary>
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            if (isUploading)
            {
                _ = FormDialogView.ShowAsync("Upload in Progress", "Please wait for upload to complete", DialogSeverity.Warning);
                return;
            }

            Close();
        }

        /// <summary>
        /// Lightweight per-field record backing each list checkbox. Parity with the WinForms private
        /// nested <c>FieldInfo</c> class. (Shadows <c>System.Reflection.FieldInfo</c>, which is not imported.)
        /// </summary>
        private sealed class FieldInfo
        {
            public string Name { get; set; }
            public string DirectoryPath { get; set; }
            public bool IsOnCloud { get; set; }
            public Guid? CloudFieldId { get; set; }
        }

        /// <summary>
        /// The three outcomes of a duplicate-name / different-id prompt. Preserved verbatim from the source
        /// for parity; <see cref="CreateNew"/> is the implicit-else branch that the OK/Cancel dialog never
        /// produces (see <see cref="AskDuplicateNameChoice"/>).
        /// </summary>
        private enum DuplicateNameChoice
        {
            Overwrite,
            CreateNew,
            Cancel
        }
    }
}
