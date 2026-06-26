// [XPLAT] migrated from net48/WinForms (Forms/FormHelp.cs) — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Help / community-links dialog.
    ///
    /// 1:1 parity reimplementation of the WinForms <c>Forms/FormHelp</c> (FormHelp.cs +
    /// FormHelp.Designer.cs). The WinForms form showed three QR codes and four buttons; the GitHub,
    /// Discourse, and YouTube buttons each opened a community URL via <c>Process.Start</c>, and Close
    /// dismissed the dialog. The original wired these four Click handlers in code (FormHelp.Designer.cs),
    /// so this markup deliberately declares only x:Name and the handlers are subscribed here.
    ///
    /// The three navigation URLs are reproduced verbatim from FormHelp.cs. <c>UseShellExecute = true</c>
    /// is set explicitly: on modern .NET <c>Process.Start(string)</c> no longer shell-executes by
    /// default, so opening an http(s) URL in the user's browser requires it — this is the documented
    /// cross-platform way to launch the default handler on Windows, Linux, and macOS.
    ///
    /// The WinForms <c>FormHelp_Load</c> also localised the four captions via <c>gStr</c> and applied an
    /// on-screen bounds clamp (<c>ScreenHelper.IsOnScreen</c>). Localisation is handled application-wide
    /// in the Avalonia migration (the captions are authored directly in the XAML here), and Avalonia
    /// centres/normalises window placement via <c>WindowStartupLocation</c>, so neither needs a
    /// per-dialog re-implementation.
    /// </summary>
    public partial class FormHelpView : Window
    {
        // [XPLAT] Community URLs — verbatim from FormHelp.cs.
        private const string GitHubUrl = "https://github.com/AgOpenGPS-Official/AgOpenGPS";
        private const string DiscourseUrl = "https://discourse.agopengps.com";
        private const string YouTubeUrl = "https://www.youtube.com/@AgOpenGPS";

        public FormHelpView()
        {
            InitializeComponent();

            buttonGitHub.Click += OnGitHubClick;
            buttonDiscourse.Click += OnDiscourseClick;
            buttonYouTube.Click += OnYouTubeClick;
            buttonClose.Click += OnCloseClick;
        }

        // [XPLAT] Parity with FormHelp.buttonGitHub_Click.
        private void OnGitHubClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(GitHubUrl);
        }

        // [XPLAT] Parity with FormHelp.buttonDiscourse_Click.
        private void OnDiscourseClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(DiscourseUrl);
        }

        // [XPLAT] Parity with FormHelp.buttonYouTube_Click.
        private void OnYouTubeClick(object sender, RoutedEventArgs e)
        {
            OpenUrl(YouTubeUrl);
        }

        // [XPLAT] buttonClose -> dismiss (WinForms DialogResult.OK). IsDefault routes Enter here too.
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Opens <paramref name="url"/> in the default browser using the shell, the
        /// cross-platform replacement for the WinForms <c>Process.Start(url)</c> behaviour.
        /// </summary>
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
