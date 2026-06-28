// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AgOpenGPS.Updater.Models;
using AgOpenGPS.Updater.Services;

namespace AgOpenGPS.Updater.Forms
{
    /// <summary>
    /// [XPLAT] Main updater window for checking and installing AgOpenGPS updates. This is the Avalonia
    /// code-behind reimplementation of the Windows Forms <c>FormUpdate</c> (<c>Forms/FormUpdate.cs</c>).
    /// The update logic (GitHub / USB detection, download, copy, install and restart) is a 1:1
    /// behavioral port and continues to flow through the unchanged <see cref="UpdateService"/> and
    /// <see cref="UsbUpdateService"/> services; only the WinForms-specific surface — Invoke /
    /// BeginInvoke, modal ShowDialog, and BackColor / Text / Enabled / Visible — is mapped to its
    /// Avalonia equivalent (<see cref="Dispatcher"/>, awaited dialogs, and Background / Content /
    /// IsEnabled / IsVisible).
    /// </summary>
    public partial class FormUpdate : Window
    {
        private enum _updateSource { Web, Local }

        private readonly UpdateService _updateService;
        private string _currentVersion;
        private _updateSource currentSource;
        private string _installPath;
        private ReleaseInfo _availableUpdate;
        private string _localUpdatePath;
        private string _localUpdateVersion;
        private bool _isBusy;
        private bool _versionFromCommandLine;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isInstalling;

        // [XPLAT] Static, reusable brushes reproducing the exact WinForms Color.FromArgb values the
        // original applied in code-behind. Allocated once (warning-clean: no per-call allocation). The
        // shared palette colors that also live in App.axaml (AccentBrush) are instead resolved at
        // runtime via GetBrush so the markup stays the single source of truth for them.
        private static readonly IBrush GrayBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100));   // #646464
        private static readonly IBrush CloseRedBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));  // #DC5050
        private static readonly IBrush CancelRedBrush = new SolidColorBrush(Color.FromRgb(200, 60, 60)); // #C83C3C
        private static readonly IBrush UpToDateBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80));    // #3C3C50

        /// <summary>
        /// [XPLAT] Parameterless constructor required by Avalonia's runtime XAML loader / design-time
        /// previewer (which instantiate via <c>Activator.CreateInstance</c>, satisfying AVLN3001). It
        /// delegates to the primary constructor with no command-line version, so it behaves exactly like
        /// a standalone launch — the <see cref="FormUpdate_Opened"/> guard shows the "must be started
        /// from AgOpenGPS" error — identical to the WinForms form when launched without
        /// <c>--current-version</c>. The real entry point remains
        /// <c>new FormUpdate(currentVersion, installPath)</c> from <c>App.axaml.cs</c>.
        /// </summary>
        public FormUpdate() : this(null, null)
        {
        }

        /// <summary>
        /// Initializes the updater window. The optional command-line-derived <paramref name="currentVersion"/>
        /// and <paramref name="installPath"/> mirror the WinForms constructor arguments; both default to
        /// <see langword="null"/> so the type stays trivially constructible and the
        /// <c>new FormUpdate(currentVersion, installPath)</c> call site in <c>App.axaml.cs</c> resolves.
        /// </summary>
        public FormUpdate(string currentVersion = null, string installPath = null)
        {
            InitializeComponent();

            _updateService = new UpdateService();
            _currentVersion = currentVersion ?? UpdateService.GetCurrentVersion();
            _installPath = installPath ?? UpdateService.GetCurrentApplicationPath();
            currentSource = _updateSource.Web;

            // [XPLAT] replaces the WinForms designer wiring `this.Load += FormUpdate_Load`.
            Opened += FormUpdate_Opened;

            // Handle command line arguments
            ParseCommandLineArgs();
        }

        /// <summary>
        /// [XPLAT] Resolves a named palette brush (for example <c>"AccentBrush"</c>) from the
        /// application-level resources declared in <c>App.axaml</c>, keeping that markup the single
        /// source of truth for the updater color scheme. Returns <see cref="Brushes.Transparent"/> only
        /// if the application or resource is unavailable (for example at design time); App.axaml always
        /// defines these keys at runtime.
        /// </summary>
        private IBrush GetBrush(string key)
        {
            if (Application.Current != null &&
                Application.Current.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) &&
                value is IBrush brush)
            {
                return brush;
            }

            return Brushes.Transparent;
        }

        private void ParseCommandLineArgs()
        {
            var args = Environment.GetCommandLineArgs();

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.Equals("--current-version", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _currentVersion = args[++i];
                    _versionFromCommandLine = true;
                }
                else if (arg.Equals("--install-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _installPath = args[++i];
                }
                else if (arg.Equals("--include-prerelease", StringComparison.OrdinalIgnoreCase))
                {
                    ChkIncludePrerelease.IsChecked = true;
                }
                else if (arg.Equals("--auto-check", StringComparison.OrdinalIgnoreCase))
                {
                    // Auto-check for updates on load
                    // [XPLAT] replaces WinForms BeginInvoke(new Action(async () => await CheckForUpdatesAsync()))
                    Dispatcher.UIThread.Post(async () => await CheckForUpdatesAsync());
                }
            }
        }

        /// <summary>
        /// [XPLAT] Equivalent of the WinForms <c>FormUpdate_Load</c> handler, wired to the
        /// <see cref="Window.Opened"/> event so the modal child dialogs (which require an already-open
        /// owner) work correctly.
        /// </summary>
        private async void FormUpdate_Opened(object sender, EventArgs e)
        {
            // Check if updater was started by AgOpenGPS (version passed via command line)
            if (!_versionFromCommandLine)
            {
                // Show error dialog and close
                await FormDialog.ShowError(this, "Updater Error",
                    "The updater must be started from AgOpenGPS.\n\n" +
                    "Please start AgOpenGPS first and use:\n" +
                    "Menu → Tools → Check for Updates\n\n" +
                    "This is required to detect your current version.");

                // [XPLAT] replaces WinForms this.BeginInvoke(new Action(() => this.Close())); deferring the
                // close onto the dispatcher avoids re-entrancy from inside the Opened handler.
                Dispatcher.UIThread.Post(() => Close());
                return;
            }

            // Display current version
            LblCurrentVersion.Text = $"Current Version: {_currentVersion}";

            // Auto-detect local update
            bool foundLocal = CheckForLocalUpdate();

            // If local update found, switch to local and enable install
            if (foundLocal && _localUpdatePath != null)
            {
                currentSource = _updateSource.Local;
                string displayVersion = !string.IsNullOrEmpty(_localUpdateVersion) ? $"v{_localUpdateVersion}" : "Local";
                _availableUpdate = new ReleaseInfo
                {
                    TagName = _localUpdateVersion ?? "Local",
                    Name = $"Local File {displayVersion}",
                    Prerelease = false,
                    PublishedAt = File.GetLastWriteTime(_localUpdatePath),
                    HtmlUrl = null
                };
                UpdateUIState(true);
            }
            else
            {
                UpdateUIState(false);
            }

            UpdateSourceUI();
        }

        private bool CheckForLocalUpdate()
        {
            var (found, filePath, version, _, message) = UsbUpdateService.CheckForLocalUpdate();
            _localUpdatePath = filePath;
            _localUpdateVersion = version;

            LblSourceInfo.Text = message;
            LblSourceInfo.IsVisible = true;

            return found;
        }

        private void UpdateSourceUI()
        {
            if (currentSource == _updateSource.Web)
            {
                BtnToggleSource.Content = "Use USB";
                BtnToggleSource.Background = GrayBrush;
                BtnToggleSource.IsEnabled = true;
                BtnCheckForUpdates.IsEnabled = !(_isInstalling && _isBusy);
                BtnCheckForUpdates.IsVisible = true;

                if (_localUpdatePath != null && !string.IsNullOrEmpty(_localUpdateVersion))
                {
                    LblSourceInfo.Text = $"Web update (Local v{_localUpdateVersion} available)";
                }
                else if (_localUpdatePath != null)
                {
                    LblSourceInfo.Text = "Web update (Local update available)";
                }
                else
                {
                    LblSourceInfo.Text = "Web update (GitHub Releases)";
                }
            }
            else
            {
                BtnToggleSource.Content = "Use Web";
                BtnToggleSource.Background = GetBrush("AccentBrush");
                BtnToggleSource.IsEnabled = true;
                BtnCheckForUpdates.IsEnabled = false;
                BtnCheckForUpdates.IsVisible = false;

                if (_localUpdatePath != null)
                {
                    string versionText = !string.IsNullOrEmpty(_localUpdateVersion) ? $" v{_localUpdateVersion}" : "";
                    LblSourceInfo.Text = $"Local{versionText}: {Path.GetFileName(_localUpdatePath)}";
                }
                else
                {
                    LblSourceInfo.Text = "Local: No AgOpenGPS_*.zip found on USB";
                }
            }

            // Update button text and state
            if (_availableUpdate != null || _localUpdatePath != null)
            {
                BtnInstallUpdate.IsEnabled = !(_isInstalling && _isBusy);
            }
            else
            {
                BtnInstallUpdate.IsEnabled = false;
            }
        }

        private void BtnToggleSource_Click(object sender, RoutedEventArgs e)
        {
            // Toggle between Web and Local source
            if (currentSource == _updateSource.Web)
            {
                currentSource = _updateSource.Local;
            }
            else
            {
                currentSource = _updateSource.Web;
            }

            // Re-check for update from new source
            if (currentSource == _updateSource.Local)
            {
                CheckForLocalUpdate();

                // Create ReleaseInfo for local file if found
                if (_localUpdatePath != null)
                {
                    string displayVersion = !string.IsNullOrEmpty(_localUpdateVersion) ? $"v{_localUpdateVersion}" : "Local";
                    _availableUpdate = new ReleaseInfo
                    {
                        TagName = _localUpdateVersion ?? "Local",
                        Name = $"Local File {displayVersion}",
                        Prerelease = false,
                        PublishedAt = File.GetLastWriteTime(_localUpdatePath),
                        HtmlUrl = null
                    };

                    UpdateUIState(true);
                }
                else
                {
                    UpdateUIState(false);
                }
            }
            else
            {
                LblSourceInfo.Text = "Web update - Click Check for Updates";
                UpdateUIState(_availableUpdate != null);
            }

            UpdateSourceUI();
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;

            BtnCheckForUpdates.IsEnabled = !busy && !_isInstalling;
            BtnInstallUpdate.IsEnabled = !busy && _availableUpdate != null && !_isInstalling;
            BtnViewReleaseNotes.IsEnabled = !busy && _availableUpdate != null &&
                !string.IsNullOrEmpty(_availableUpdate.Body) && !_isInstalling;
            ChkIncludePrerelease.IsEnabled = !busy && !_isInstalling;

            // Close button changes to Cancel when installing
            if (_isInstalling)
            {
                BtnClose.Content = "Cancel";
                BtnClose.Background = CancelRedBrush; // Red
                BtnClose.IsEnabled = true;
            }
            else
            {
                BtnClose.Content = busy ? "Please wait..." : "Close";
                BtnClose.Background = CloseRedBrush; // Lighter red
                BtnClose.IsEnabled = !busy;
            }
        }

        private void UpdateUIState(bool hasUpdate)
        {
            BtnInstallUpdate.IsEnabled = hasUpdate && !_isInstalling;

            // Enable View Release Notes button if update has release notes
            BtnViewReleaseNotes.IsEnabled = hasUpdate && _availableUpdate != null &&
                !string.IsNullOrEmpty(_availableUpdate.Body) && !_isInstalling;

            if (hasUpdate && _availableUpdate != null)
            {
                LblLatestVersion.Text = $"Latest Version: {_availableUpdate.Version} (New!)";
                LblLatestVersion.Foreground = GetBrush("AccentBrush");
            }
            else
            {
                LblLatestVersion.Text = "Latest Version: Up to date";
                LblLatestVersion.Foreground = UpToDateBrush;
            }
        }

        private void SetStatus(string message, bool isProgress = false, int progressPercent = 0)
        {
            // [XPLAT] marshal to the UI thread (replaces the WinForms InvokeRequired/Invoke recursion).
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetStatus(message, isProgress, progressPercent));
                return;
            }

            LblStatus.Text = message;
            ProgressBar1.IsVisible = isProgress;
            LblProgressPercent.IsVisible = isProgress;

            if (isProgress)
            {
                ProgressBar1.Value = progressPercent;
                LblProgressPercent.Text = $"{progressPercent}%";
            }
            else
            {
                ProgressBar1.Value = 0;
                LblProgressPercent.Text = "0%";
            }
        }

        private async void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync();
        }

        private async void BtnViewReleaseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null)
                return;

            string notes = !string.IsNullOrEmpty(_availableUpdate.Body)
                ? _availableUpdate.Body
                : "No release notes available.";

            await FormReleaseNotes.ShowReleaseNotes(this, "AgOpenGPS Update", _availableUpdate.Version, notes);
        }

        private async Task CheckForUpdatesAsync()
        {
            if (_isBusy)
                return;

            try
            {
                SetBusy(true);

                if (currentSource == _updateSource.Web)
                {
                    SetStatus("Checking GitHub releases...");
                    bool includePrerelease = ChkIncludePrerelease.IsChecked == true;
                    var (hasUpdate, releaseInfo, message) = await _updateService.CheckForUpdate(
                        _currentVersion, includePrerelease);

                    _availableUpdate = releaseInfo;

                    if (hasUpdate && releaseInfo != null)
                    {
                        SetStatus($"Update available: {releaseInfo.Version} ({releaseInfo.ReleaseType})");
                        UpdateUIState(true);
                        UpdateSourceUI();
                    }
                    else
                    {
                        SetStatus(message);
                        UpdateUIState(false);
                    }
                }
                else // Local source
                {
                    SetStatus("Checking USB drive...");
                    await Task.Delay(500); // Brief pause for UI update

                    var (found, filePath, version, _, _) = UsbUpdateService.CheckForLocalUpdate();
                    _localUpdatePath = filePath;
                    _localUpdateVersion = version;

                    if (found)
                    {
                        string displayVersion = string.IsNullOrEmpty(version) ? "Local" : $"v{version}";
                        _availableUpdate = new ReleaseInfo
                        {
                            TagName = version ?? "Local",
                            Name = $"Local File {displayVersion}",
                            Prerelease = false,
                            PublishedAt = File.GetLastWriteTime(filePath),
                            HtmlUrl = null
                        };

                        SetStatus($"Local update found: {displayVersion}");
                        UpdateUIState(true);
                        UpdateSourceUI();
                    }
                    else
                    {
                        SetStatus("No update found on USB drive.");
                        UpdateUIState(false);
                        UpdateSourceUI();
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
                UpdateUIState(false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void BtnInstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_availableUpdate == null)
                return;

            // Show confirmation dialog FIRST on UI thread
            string updateSource = currentSource == _updateSource.Web ? "GitHub" : "USB";
            string updateInfo = currentSource == _updateSource.Web
                ? $"{_availableUpdate.Version} ({_availableUpdate.ReleaseType})"
                : UsbUpdateService.GetUpdateLocation(_localUpdatePath);

            bool result = await FormDialog.ShowConfirm(
                this,
                "Install Update",
                $"Install update from {updateSource}?\n\nSource: {updateInfo}\n\nThis will:\n" +
                "• Close AgOpenGPS and AgIO\n" +
                "• Create a backup of your current installation\n" +
                "• Install the update\n" +
                "• Restart AgOpenGPS\n\n" +
                "Do you want to continue?",
                "Install",
                "Cancel");

            if (!result)
                return;

            // Prepare UI on UI thread BEFORE starting background work
            if (_isBusy)
                return;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            _isInstalling = true;
            SetBusy(true);
            ProgressBar1.IsVisible = true;
            ProgressBar1.Value = 0;

            // Start installation - runs on background thread, UI updates marshaled via Progress<T>
            await InstallUpdateAsync(_cancellationTokenSource.Token);
        }

        private async Task InstallUpdateAsync(CancellationToken token)
        {
            try
            {
                // Step 1: Close applications
                SetStatus("Closing AgOpenGPS and AgIO...", true);
                ProgressBar1.Value = 10;

                var (closed, closeMsg) = await _updateService.CloseApplicationsAsync();
                if (!closed)
                {
                    // Non-fatal warning, continue
                    SetStatus($"Warning: {closeMsg}");
                }

                // Wait for applications to close
                await Task.Delay(2000);
                SetStatus("Applications closed", true, 20);

                // Check for cancellation
                token.ThrowIfCancellationRequested();

                string downloadPath;

                if (currentSource == _updateSource.Web)
                {
                    // Step 2: Download from GitHub
                    string tempDir = Path.Combine(Path.GetTempPath(), "AgOpenGPS_Update");
                    SetStatus("Downloading from GitHub...", true, 30);

                    var progress = new Progress<double>(percent =>
                    {
                        int overallProgress = 30 + (int)(percent * 0.5); // 30-80% range
                        SetStatus($"Downloading... {(int)percent}%", true, overallProgress);
                    });

                    var (downloaded, dlPath, downloadMsg) = await _updateService.DownloadUpdate(
                        _availableUpdate, tempDir, progress);

                    token.ThrowIfCancellationRequested();

                    if (!downloaded)
                    {
                        await ShowErrorFromBackground("Download Failed", $"Failed to download update:\n\n{downloadMsg}");
                        _isInstalling = false;
                        SetBusy(false);
                        SetStatus("Download failed");
                        return;
                    }

                    downloadPath = dlPath;

                    // Step 3: Install update with progress
                    SetStatus("Installing...", true, 85);
                }
                else
                {
                    // Local file - copy to temp first (like web update)
                    string tempDir = Path.Combine(Path.GetTempPath(), "AgOpenGPS_Update");
                    Directory.CreateDirectory(tempDir);

                    string tempFileName = Path.GetFileName(_localUpdatePath);
                    downloadPath = Path.Combine(tempDir, tempFileName);

                    // Copy with progress
                    SetStatus("Copying from USB...", true, 30);

                    try
                    {
                        var fileProgress = new Progress<double>(percent =>
                        {
                            int overallProgress = 30 + (int)(percent * 0.5); // 30-80% range
                            SetStatus($"Copying... {(int)percent}%", true, overallProgress);
                        });

                        await CopyFileWithProgressAsync(_localUpdatePath, downloadPath, fileProgress, token);
                    }
                    catch (Exception ex)
                    {
                        await ShowErrorFromBackground("Copy Failed", $"Failed to copy from USB:\n\n{ex.Message}");
                        _isInstalling = false;
                        SetBusy(false);
                        SetStatus("Copy failed");
                        return;
                    }

                    SetStatus("Installing...", true, 85);
                }

                // Install update with progress
                var installProgress = new Progress<UpdateService.InstallProgress>(progressInfo =>
                {
                    SetStatus(progressInfo.Phase, true, progressInfo.OverallPercent);
                });

                var (installed, installMsg) = await _updateService.InstallUpdateAsync(
                    downloadPath, _installPath, _currentVersion, installProgress);

                token.ThrowIfCancellationRequested();

                if (!installed)
                {
                    await ShowErrorFromBackground("Installation Failed", $"Failed to install update:\n\n{installMsg}");
                    _isInstalling = false;
                    SetBusy(false);
                    SetStatus("Installation failed");
                    return;
                }

                // Clean up temp file (both web and local)
                try
                {
                    if (File.Exists(downloadPath))
                    {
                        File.Delete(downloadPath);
                    }
                }
                catch { }

                // Restart application
                SetStatus("Restarting...", true, 100);
                await Task.Delay(1000);

                // [XPLAT] RestartApplication releases the updater single-instance guard internally; the
                // WinForms code likewise read the returned (success, message) tuple but did not surface it.
                _ = _updateService.RestartApplication(_installPath);

                // Show success message briefly
                SetStatus("Complete! Restarting...");
                await Task.Delay(2000);

                // Close updater
                Close();
            }
            catch (OperationCanceledException)
            {
                // User cancelled the operation
                _isInstalling = false;
                SetBusy(false);
                SetStatus("Cancelled");
                ProgressBar1.IsVisible = false;

                await ShowInfoFromBackground("Cancelled", "Update was cancelled.");
            }
            catch (Exception ex)
            {
                _isInstalling = false;
                SetBusy(false);
                SetStatus($"Error: {ex.Message}");
                ProgressBar1.IsVisible = false;

                await ShowErrorFromBackground("Error", $"An error occurred:\n\n{ex.Message}");
            }
        }

        // [XPLAT] marshal a modal error dialog onto the UI thread (replaces the WinForms InvokeRequired/Invoke).
        private async Task ShowErrorFromBackground(string title, string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await FormDialog.ShowError(this, title, message);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => FormDialog.ShowError(this, title, message));
            }
        }

        // [XPLAT] marshal a modal info dialog onto the UI thread (replaces the WinForms InvokeRequired/Invoke).
        private async Task ShowInfoFromBackground(string title, string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                await FormDialog.ShowInfo(this, title, message);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => FormDialog.ShowInfo(this, title, message));
            }
        }

        private async Task CopyFileWithProgressAsync(
            string sourcePath, string destPath, IProgress<double> progress, CancellationToken token)
        {
            const int bufferSize = 1024 * 1024; // 1MB buffer

            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
            using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.WriteThrough))
            {
                long totalBytes = sourceStream.Length;
                long copiedBytes = 0;
                byte[] buffer = new byte[bufferSize];

                int bytesRead;
                while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                {
                    await destStream.WriteAsync(buffer, 0, bytesRead, token);
                    copiedBytes += bytesRead;

                    double percent = (double)copiedBytes / totalBytes * 100.0;
                    progress.Report(percent);
                }
            }
        }

        private async void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (_isInstalling)
            {
                // Cancel the ongoing operation
                _cancellationTokenSource?.Cancel();
                SetStatus("Cancelling...");
            }
            else if (_isBusy)
            {
                // Busy but not installing - can't cancel
                await FormDialog.ShowInfo(this, "Please Wait", "Please wait for the current operation to complete.");
            }
            else
            {
                // Just close the form
                Close();
            }
        }
    }
}
