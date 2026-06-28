// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Help / community-links dialog.
    ///
    /// 1:1 behavioural parity reimplementation of the WinForms <c>Forms/FormHelp</c>
    /// (FormHelp.cs + FormHelp.Designer.cs). The WinForms form showed three QR codes above
    /// three external-link buttons (GitHub, Discourse, YouTube) plus a Close button; each link
    /// button opened a community URL via <c>Process.Start</c>, and Close dismissed the dialog
    /// (WinForms <c>DialogResult.OK</c>). The original subscribed those Click handlers in code
    /// (FormHelp.Designer.cs), so the paired <c>FormHelpView.axaml</c> declares only
    /// <c>x:Name</c> on each control and the handlers are wired here in the code-behind.
    ///
    /// Visual layout (sizes, positions, fonts, glyphs) lives entirely in the partner
    /// <c>FormHelpView.axaml</c>; this code-behind owns only the dialog's behaviour:
    /// caption localisation, button wiring, and the cross-platform browser launch.
    /// </summary>
    public partial class FormHelpView : Window
    {
        // [XPLAT] Community URLs — copied verbatim from FormHelp.cs (must not drift).
        private const string GitHubUrl = "https://github.com/AgOpenGPS-Official/AgOpenGPS";
        private const string DiscourseUrl = "https://discourse.agopengps.com";
        private const string YouTubeUrl = "https://www.youtube.com/@AgOpenGPS";

        /// <summary>
        /// Parameterless constructor — parity with the WinForms <c>FormHelp()</c> ctor. The host
        /// constructs this dialog and shows it modally over <c>FormGPS</c> (WinForms
        /// <c>StartPosition.CenterParent</c> → the markup's <c>WindowStartupLocation="CenterOwner"</c>).
        /// </summary>
        public FormHelpView()
        {
            InitializeComponent();

            // Subscribe the four button Click handlers in code, mirroring FormHelp.Designer.cs
            // (which wired buttonGitHub_Click / buttonDiscourse_Click / buttonYouTube_Click in code).
            // The .axaml deliberately declares only x:Name (no Click="...") so markup and code-behind
            // can be authored independently.
            buttonGitHub.Click += OnGitHubClick;
            buttonDiscourse.Click += OnDiscourseClick;
            buttonYouTube.Click += OnYouTubeClick;
            buttonClose.Click += OnCloseClick;

            // Localise captions at construction time (parity with FormHelp_Load). The named controls
            // are available immediately after InitializeComponent(), so no deferral to a Loaded event
            // is needed for static caption assignment.
            ApplyTranslations();
        }

        /// <summary>
        /// [XPLAT] Caption localisation — direct parity with the WinForms <c>FormHelp_Load</c>, which
        /// assigned each caption from <see cref="gStr"/>. The four button captions are set on the
        /// label <c>TextBlock</c>s by name (NOT <c>Button.Content</c>, which holds the icon-over-caption
        /// layout authored in the markup). Note the GitHub button caption is
        /// <see cref="gStr.gsCheckForUpdates"/> ("Check for Updates"), exactly as in FormHelp.cs.
        ///
        /// [XPLAT] The WinForms <c>FormHelp_Load</c> also ran a <c>ScreenHelper.IsOnScreen(Bounds)</c>
        /// guard that reset Top/Left to (0,0) when the form fell outside the desktop bounds. That guard
        /// is a Windows-screen-bounds helper and is intentionally omitted here: Avalonia's
        /// <c>WindowStartupLocation="CenterOwner"</c> (set in the markup) handles correct placement
        /// across Windows, macOS, and Linux.
        /// </summary>
        private void ApplyTranslations()
        {
            Title = gStr.gsHelp;
            buttonGitHubLabel.Text = gStr.gsCheckForUpdates;
            buttonDiscourseLabel.Text = gStr.gsDiscourseForum;
            buttonYouTubeLabel.Text = gStr.gsYouTubeTutorials;
            buttonCloseLabel.Text = gStr.gsClose;
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

        /// <summary>
        /// buttonClose → dismiss the dialog (parity with the WinForms <c>buttonClose</c>
        /// <c>DialogResult.OK</c>). The markup marks this button <c>IsDefault="True"</c>, so the
        /// Enter key also routes here.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// [XPLAT] Opens <paramref name="url"/> in the user's default browser. Replaces the WinForms
        /// <c>Process.Start(url)</c>: on modern .NET a bare <c>Process.Start(string)</c> does NOT
        /// shell-execute and throws for an http(s) URL, so <see cref="ProcessStartInfo.UseShellExecute"/>
        /// is set explicitly. This is the canonical cross-platform browser launch and works on
        /// Windows, macOS, and Linux.
        /// </summary>
        private static void OpenUrl(string url)
        {
            // [XPLAT] cross-platform default-browser launch (replaces bare Process.Start(url)).
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}
