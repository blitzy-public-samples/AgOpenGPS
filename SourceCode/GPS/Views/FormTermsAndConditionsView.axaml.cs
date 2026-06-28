// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AgOpenGPS.Core.Translations;

namespace AgOpenGPS.Views
{
    /// <summary>
    /// [XPLAT] Terms &amp; Conditions / EULA startup-gate dialog.
    ///
    /// 1:1 behavioural-parity reimplementation of the WinForms <c>Forms/FormTermsAndConditions</c>
    /// (FormTermsAndConditions.cs + FormTermsAndConditions.Designer.cs). It is the modal gate the
    /// operator must "Agree" to before AgOpenGPS proceeds: it shows a warning glyph, the
    /// product version, the educational-use disclaimer, the Apache 2.0 licence text, three
    /// community-link buttons (GitHub / Discourse / YouTube), and an Agree / Disagree pair.
    /// </summary>
    /// <remarks>
    /// Imperative, code-behind-driven dialog — there is intentionally no view-model, no
    /// <c>DataContext</c>, no <c>x:DataType</c> and no <c>{Binding}</c>; the x:Name'd controls
    /// declared in the partner <c>FormTermsAndConditionsView.axaml</c> are the entire integration
    /// surface (parity with the parameterless WinForms <c>FormTermsAndConditions()</c> constructor
    /// plus <c>Form_About_Load</c>). The behaviour reproduced here:
    /// <list type="bullet">
    ///   <item><see cref="ApplyTranslations"/> mirrors <c>Form_About_Load</c>: it assigns the eight
    ///   localized captions from <see cref="gStr"/> and the live product version from
    ///   <c>AgOpenGPS.Program.SemVer</c> at construction time (Avalonia has no separate
    ///   <c>Load</c> event to defer to).</item>
    ///   <item>The three link buttons open their community URLs in the operator's default browser via
    ///   <see cref="OpenUrl"/> — the cross-platform replacement for the WinForms
    ///   <c>Process.Start(url)</c> (see <see cref="OnGitHubClick"/> /
    ///   <see cref="OnDiscourseClick"/> / <see cref="OnYouTubeClick"/>).</item>
    ///   <item>Agree (WinForms <c>DialogResult.OK</c>) closes returning <see langword="true"/> and
    ///   Disagree (WinForms <c>DialogResult.Cancel</c>) closes returning <see langword="false"/> via
    ///   <c>ShowDialog&lt;bool&gt;</c>; the caller exits the application when the operator does not
    ///   agree, preserving the original gating semantics.</item>
    /// </list>
    /// Two WinForms-only behaviours are deliberately not reproduced and are noted inline below:
    /// the 10px CornflowerBlue frame painted in <c>OnPaintBackground</c> with a GDI <c>Pen</c> is
    /// now the declarative <c>Border</c> in the XAML, and the <c>ScreenHelper.IsOnScreen</c> bounds
    /// clamp is unnecessary because Avalonia's <c>WindowStartupLocation="CenterOwner"</c> handles
    /// placement.
    /// </remarks>
    public partial class FormTermsAndConditionsView : Window
    {
        // [XPLAT] Community URLs — verbatim from FormTermsAndConditions.cs (same three constants as FormHelpView).
        private const string GitHubUrl = "https://github.com/AgOpenGPS-Official/AgOpenGPS";
        private const string DiscourseUrl = "https://discourse.agopengps.com";
        private const string YouTubeUrl = "https://www.youtube.com/@AgOpenGPS";

        /// <summary>
        /// Initializes the dialog: loads the compiled XAML, applies the localized captions and live
        /// version (parity with <c>Form_About_Load</c>), and subscribes the button Click handlers.
        /// </summary>
        public FormTermsAndConditionsView()
        {
            InitializeComponent();

            // [XPLAT] Form_About_Load parity — localized captions + live version applied here because
            // Avalonia has no WinForms Load event. The WinForms ScreenHelper.IsOnScreen on-screen
            // clamp is intentionally omitted: WindowStartupLocation="CenterOwner" handles placement.
            ApplyTranslations();

            // [XPLAT] Handlers wired in code, parity with FormTermsAndConditions.Designer.cs (which
            // wired the three link-button Click events; labelAgree/labelDisagree carried
            // DialogResult.OK/Cancel). The XAML declares only x:Name, so wiring lives here.
            buttonGitHub.Click += OnGitHubClick;
            buttonDiscourse.Click += OnDiscourseClick;
            buttonYouTube.Click += OnYouTubeClick;
            labelAgree.Click += OnAgreeClick;
            labelDisagree.Click += OnDisagreeClick;
        }

        /// <summary>
        /// [XPLAT] Mirrors <c>FormTermsAndConditions.Form_About_Load</c>: assigns every runtime caption
        /// from <see cref="gStr"/> and the live product version, replacing the design-time English
        /// placeholders carried in the XAML so the dialog honours the active UI culture exactly as the
        /// WinForms original did.
        /// </summary>
        private void ApplyTranslations()
        {
            // Standalone labels are x:Name'd TextBlocks — assigned directly.
            labelTermsAndConditions.Text = gStr.gsTermsConditions;
            labelVersion.Text = gStr.gsVersion + ":";
            labelTerms.Text = gStr.gsTerms;

            // [XPLAT] Live product version — the same value the WinForms version label showed
            // (Program.SemVer in the GPS root). Replaces the "x.y.z" design-time placeholder.
            labelVersionActual.Text = AgOpenGPS.Program.SemVer;

            // The five buttons render an icon above a caption (a Grid of Image + TextBlock), so the
            // localized caption is applied to each button's inner TextBlock rather than its Content.
            SetButtonCaption(buttonGitHub, gStr.gsCheckForUpdates);
            SetButtonCaption(buttonDiscourse, gStr.gsDiscourseForum);
            SetButtonCaption(buttonYouTube, gStr.gsYouTubeTutorials);
            SetButtonCaption(labelAgree, gStr.gsAgree);
            SetButtonCaption(labelDisagree, gStr.gsDisagree);
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

        // [XPLAT] labelAgree -> WinForms DialogResult.OK. The licence was accepted; the program proceeds.
        private void OnAgreeClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        // [XPLAT] labelDisagree -> WinForms DialogResult.Cancel. The licence was declined; the caller exits.
        private void OnDisagreeClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        /// <summary>
        /// [XPLAT] Applies <paramref name="text"/> to the caption <see cref="TextBlock"/> nested inside
        /// an icon-over-caption <see cref="Button"/> (its <see cref="ContentControl.Content"/> is a
        /// <see cref="Panel"/> holding the glyph image and the caption). Reproduces the WinForms
        /// <c>button.Text</c> assignment for buttons whose face is an image plus a label. If the
        /// expected structure is absent the button keeps its design-time caption, so this degrades
        /// gracefully and never throws.
        /// </summary>
        /// <param name="button">The image-and-caption button to caption.</param>
        /// <param name="text">The localized caption to display.</param>
        private static void SetButtonCaption(Button button, string text)
        {
            if (button.Content is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBlock caption)
                    {
                        caption.Text = text;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// [XPLAT] Opens <paramref name="url"/> in the operator's default browser using the shell — the
        /// cross-platform replacement for the WinForms <c>Process.Start(url)</c>. On modern .NET
        /// <c>Process.Start(string)</c> no longer shell-executes by default, so <c>UseShellExecute</c>
        /// must be set explicitly to launch the OS default handler on Windows, Linux, and macOS.
        /// </summary>
        /// <param name="url">The absolute http(s) URL to open.</param>
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
