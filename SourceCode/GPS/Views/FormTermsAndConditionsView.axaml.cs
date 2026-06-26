// [XPLAT] migrated from net48/WinForms (Forms/FormTermsAndConditions.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Terms &amp; Conditions / About dialog.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormTermsAndConditions</c>
    /// (FormTermsAndConditions.cs + .Designer.cs). The WinForms form displayed the licence text and
    /// version, three community-link buttons (GitHub / Discourse / YouTube) that opened a URL via
    /// <c>Process.Start</c>, and an Agree / Disagree pair that closed with
    /// <c>DialogResult.OK</c> / <c>DialogResult.Cancel</c>. The handlers were wired in
    /// FormTermsAndConditions.Designer.cs, so this markup declares only x:Name and they are
    /// subscribed here.
    ///
    /// The three URLs are reproduced verbatim from FormTermsAndConditions.cs; <c>UseShellExecute</c>
    /// is set for the same cross-platform reason documented in FormHelpView. Agree closes with a
    /// <c>true</c> result and Disagree with <c>false</c>, mirroring the WinForms OK / Cancel results
    /// (consumed by the caller via <c>ShowDialog&lt;bool&gt;</c>).
    ///
    /// The WinForms <c>Form_About_Load</c> additionally localised captions via <c>gStr</c> and set
    /// <c>labelVersionActual.Text = Program.SemVer</c>. Localisation is handled application-wide in
    /// the Avalonia migration (captions authored in XAML), and the live version string is sourced
    /// from the application bootstrap; the cosmetic version label is therefore left for that bootstrap
    /// wiring rather than bound to a value that is not yet projected into the shell.
    /// </summary>
    public partial class FormTermsAndConditionsView : Window
    {
        // [XPLAT] Community URLs — verbatim from FormTermsAndConditions.cs.
        private const string GitHubUrl = "https://github.com/AgOpenGPS-Official/AgOpenGPS";
        private const string DiscourseUrl = "https://discourse.agopengps.com";
        private const string YouTubeUrl = "https://www.youtube.com/@AgOpenGPS";

        public FormTermsAndConditionsView()
        {
            InitializeComponent();

            buttonGitHub.Click += OnGitHubClick;
            buttonDiscourse.Click += OnDiscourseClick;
            buttonYouTube.Click += OnYouTubeClick;
            labelAgree.Click += OnAgreeClick;
            labelDisagree.Click += OnDisagreeClick;
        }

        // [XPLAT] Parity with FormTermsAndConditions.buttonGitHub_Click.
        private void OnGitHubClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(GitHubUrl);
        }

        // [XPLAT] Parity with FormTermsAndConditions.buttonDiscourse_Click.
        private void OnDiscourseClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(DiscourseUrl);
        }

        // [XPLAT] Parity with FormTermsAndConditions.buttonYouTube_Click.
        private void OnYouTubeClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(YouTubeUrl);
        }

        // [XPLAT] labelAgree -> DialogResult.OK (true). The licence was accepted.
        private void OnAgreeClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] labelDisagree -> DialogResult.Cancel (false). The licence was declined.
        private void OnDisagreeClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private static void OpenUrl(string url)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
