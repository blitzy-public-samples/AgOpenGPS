// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AgOpenGPS.Core.Models;
using AgOpenGPS.Core.Translations;
using AgOpenGPS.Properties;

namespace AgOpenGPS.Views.Config
{
    /// <summary>
    /// [XPLAT] Code-behind for the embedded vehicle-configuration UserControl — the behaviour half of
    /// <c>ConfigVehicleControl.axaml</c>. This is a 1:1 behavioural-parity reimplementation of the
    /// WinForms <c>Forms/Config/ConfigVehicleControl</c> (ConfigVehicleControl.cs +
    /// ConfigVehicleControl.Designer.cs). It lets the operator pick the vehicle type
    /// (Tractor / Harvester / Articulated), pick the brand logo used for the on-screen vehicle, toggle
    /// whether an image is drawn at all, and set the image opacity. Only the Windows-only mechanisms are
    /// replaced — the GDI <c>ColorMatrix</c> alpha blend becomes <see cref="Visual.Opacity"/>, the
    /// <c>PictureBox</c> becomes an Avalonia <see cref="Image"/>, and the per-brand <c>RadioButton</c>s
    /// (formerly fixed Designer controls with embedded <c>BrandImages</c>) are generated programmatically
    /// with logos loaded from packaged <c>avares://</c> assets. The data flow is unchanged (AAP §0.3.3,
    /// §0.7.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Display/imperative, with no view-model and no data binding — exactly like the WinForms original,
    /// which reached its controls directly and never used <c>FormGPS</c> (<c>mf</c>). The control is
    /// driven solely by the injected <see cref="VehicleConfig"/> plus the cross-platform
    /// <see cref="VehicleSettings"/> / <c>Settings</c> stores. The host calls
    /// <see cref="Initialize(VehicleConfig)"/> once to bind the config and render the initial state, and
    /// <see cref="UpdateSettings()"/> to flush the chosen values back into the settings stores.
    /// </para>
    /// <para>
    /// [XPLAT] The simple name <c>Settings</c> is shadowed in this <c>AgOpenGPS.Views.Config</c> context by
    /// the sibling <c>AgOpenGPS.Views.Settings</c> namespace, so every reference to the settings type is
    /// fully qualified as <see cref="AgOpenGPS.Properties.Settings"/> (<see cref="VehicleSettings"/> is not
    /// shadowed and stays a simple name). Every numeric <c>ToString</c> uses
    /// <see cref="CultureInfo.InvariantCulture"/> so the opacity readout never drifts with the host OS
    /// locale (AAP §0.6.5).
    /// </para>
    /// </remarks>
    public partial class ConfigVehicleControl : UserControl
    {
        // [XPLAT] Injected vehicle configuration. Null until Initialize(...) runs; the vehicle-type and
        // image-toggle handlers null-guard it exactly as the WinForms source did.
        private VehicleConfig _vehicleConfig;

        // Currently selected brand per vehicle type. Mirrors the WinForms backing fields. The WinForms
        // "_original" Bitmap cache is intentionally dropped — opacity is now a render property, so there
        // is no bitmap to clone/recolour (the GDI SetAlpha/ColorMatrix path is removed entirely).
        private TractorBrand _tractorBrand;
        private HarvesterBrand _harvesterBrand;
        private ArticulatedBrand _articulatedBrand;

        /// <summary>
        /// Initializes the control, assigns the localized captions from <see cref="gStr"/>, generates the
        /// brand-logo radios into their panels, and wires the interaction handlers.
        /// </summary>
        public ConfigVehicleControl()
        {
            InitializeComponent();

            labelVehicleGroupBox.Text = gStr.gsVehiclegroupbox;
            labelImage.Text = gStr.gsImage + ":";
            labelOpacity.Text = gStr.gsOpacity + ":";

            // [XPLAT] Build the 25 brand radios programmatically into the 3 panels (replaces the fixed
            // Designer RadioButtons whose images were embedded Properties.Resources / BrandImages).
            BuildBrandRadios();

            // [XPLAT] Vehicle-type radios + the image toggle use Click (NOT IsCheckedChanged) so that the
            // programmatic IsChecked=true assignments in Initialize()/UpdateImage() do NOT re-fire the
            // handler — this mirrors the WinForms "_Click" wiring exactly.
            rbtnTractor.Click += rbtnTractor_Click;
            rbtnHarvester.Click += rbtnHarvester_Click;
            rbtnArticulated.Click += rbtnArticulated_Click;
            cboxIsImage.Click += cboxIsImage_Click;

            btnOpacityDn.Click += btnOpacityDn_Click;
            btnOpacityUp.Click += btnOpacityUp_Click;
        }

        /// <summary>
        /// Gets the selected tractor brand. Setting it also refreshes the preview image (replacing the
        /// WinForms <c>pboxAlpha.BackgroundImage</c> assignment).
        /// </summary>
        public TractorBrand TractorBrand
        {
            get => _tractorBrand;
            private set
            {
                _tractorBrand = value;
                previewImage.Source = TractorBitmaps.GetBitmap(value); // [XPLAT] Avalonia IImage (was System.Drawing.Bitmap)
            }
        }

        /// <summary>
        /// Gets the selected harvester brand. Setting it also refreshes the preview image.
        /// </summary>
        public HarvesterBrand HarvesterBrand
        {
            get => _harvesterBrand;
            private set
            {
                _harvesterBrand = value;
                previewImage.Source = HarvesterBitmaps.GetBitmap(value); // [XPLAT] Avalonia IImage
            }
        }

        /// <summary>
        /// Gets the selected articulated brand. Setting it also refreshes the preview image with the
        /// front silhouette (parity with the WinForms <c>ArticulatedBitmaps.GetFrontBitmap</c> call).
        /// </summary>
        public ArticulatedBrand ArticulatedBrand
        {
            get => _articulatedBrand;
            private set
            {
                _articulatedBrand = value;
                previewImage.Source = ArticulatedBitmaps.GetFrontBitmap(value); // [XPLAT] Avalonia IImage
            }
        }

        /// <summary>
        /// Binds the supplied <see cref="VehicleConfig"/>, selects the matching vehicle-type radio, and
        /// renders the initial image/brand/opacity state. Called once by the host (<c>ConfigView</c>).
        /// </summary>
        /// <param name="vehicleConfig">The active vehicle configuration to edit.</param>
        public void Initialize(VehicleConfig vehicleConfig)
        {
            _vehicleConfig = vehicleConfig;

            switch (_vehicleConfig.Type)
            {
                case VehicleType.Tractor:
                    rbtnTractor.IsChecked = true;
                    break;
                case VehicleType.Harvester:
                    rbtnHarvester.IsChecked = true;
                    break;
                case VehicleType.Articulated:
                    rbtnArticulated.IsChecked = true;
                    break;
            }

            UpdateImage();
        }

        /// <summary>
        /// Flushes the current vehicle-type, brand, image-on, opacity, and vehicle colour selections into
        /// the cross-platform settings stores. Parity with the WinForms <c>UpdateSettings()</c>.
        /// </summary>
        public void UpdateSettings()
        {
            switch (_vehicleConfig.Type)
            {
                case VehicleType.Tractor:
                    VehicleSettings.Default.setVehicle_vehicleType = 0;
                    // [XPLAT] setBrand_TBrand is the TractorBrand enum in the migrated VehicleSettings
                    // (not an int) — assign the enum directly, exactly as the WinForms source did.
                    VehicleSettings.Default.setBrand_TBrand = _tractorBrand;
                    break;
                case VehicleType.Harvester:
                    VehicleSettings.Default.setVehicle_vehicleType = 1;
                    VehicleSettings.Default.setBrand_HBrand = _harvesterBrand;
                    break;
                case VehicleType.Articulated:
                    VehicleSettings.Default.setVehicle_vehicleType = 2;
                    VehicleSettings.Default.setBrand_WDBrand = _articulatedBrand;
                    break;
            }

            // [XPLAT] Fully qualify AgOpenGPS.Properties.Settings — the simple name "Settings" is shadowed
            // by the AgOpenGPS.Views.Settings namespace from this AgOpenGPS.Views.Config context.
            AgOpenGPS.Properties.Settings.Default.setDisplay_isVehicleImage = _vehicleConfig.IsImage;
            AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity = (int)(_vehicleConfig.Opacity * 100);
            // [XPLAT] System.Drawing.Primitives Color (data only, NOT GDI); fully qualified so it does not
            // collide with Avalonia.Media.Color. ColorRgba defines the explicit System.Drawing.Color operator.
            AgOpenGPS.Properties.Settings.Default.setDisplay_colorVehicle = (System.Drawing.Color)_vehicleConfig.Color;
        }

        /// <summary>
        /// Shows the brand panel for the active vehicle type (hiding the others), loads the persisted
        /// brand selection, or — when images are disabled — shows the neutral triangle logo. Always resets
        /// the vehicle colour to white, syncs the "no image" checkbox, and re-applies opacity. Verbatim
        /// parity with the WinForms <c>UpdateImage()</c>.
        /// </summary>
        private void UpdateImage()
        {
            panelTractorBrands.IsVisible = panelHarvesterBrands.IsVisible = panelArticulatedBrands.IsVisible = false;

            if (_vehicleConfig.IsImage)
            {
                switch (_vehicleConfig.Type)
                {
                    case VehicleType.Tractor:
                        panelTractorBrands.IsVisible = true;
                        // [XPLAT] enum-typed setting read directly (no (int) cast — migrated type is the enum).
                        TractorBrand = VehicleSettings.Default.setBrand_TBrand;
                        UpdateTractorBrand();
                        break;
                    case VehicleType.Harvester:
                        panelHarvesterBrands.IsVisible = true;
                        HarvesterBrand = VehicleSettings.Default.setBrand_HBrand;
                        UpdateHarvesterBrand();
                        break;
                    case VehicleType.Articulated:
                        panelArticulatedBrands.IsVisible = true;
                        ArticulatedBrand = VehicleSettings.Default.setBrand_WDBrand;
                        UpdateArticulatedBrand();
                        break;
                }

                AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity = (int)(_vehicleConfig.Opacity * 100);
            }
            else
            {
                // [XPLAT] avares logo replaces the WinForms BrandImages.BrandTriangleVehicle resource.
                previewImage.Source = LoadBrandLogo("BrandTriangleVehicle");
            }

            _vehicleConfig.Color = new ColorRgba(254, 254, 254);

            cboxIsImage.IsChecked = !_vehicleConfig.IsImage;
            ResetImage();
        }

        /// <summary>Selects the tractor brand radio matching the current <see cref="_tractorBrand"/>.</summary>
        private void UpdateTractorBrand()
        {
            CheckBrandRadio(panelTractorBrands, _tractorBrand);
        }

        /// <summary>Selects the harvester brand radio matching the current <see cref="_harvesterBrand"/>.</summary>
        private void UpdateHarvesterBrand()
        {
            CheckBrandRadio(panelHarvesterBrands, _harvesterBrand);
        }

        /// <summary>Selects the articulated brand radio matching the current <see cref="_articulatedBrand"/>.</summary>
        private void UpdateArticulatedBrand()
        {
            CheckBrandRadio(panelArticulatedBrands, _articulatedBrand);
        }

        /// <summary>
        /// [XPLAT] WinForms set <c>rbtnBrandX.Checked = true</c> on the one radio matching the brand. In
        /// the programmatic model we iterate the panel's generated radios and check the one whose
        /// <see cref="Control.Tag"/> equals the current brand enum (which fires its
        /// <c>IsCheckedChanged</c> → sets the typed property → refreshes the preview).
        /// </summary>
        /// <param name="panel">The brand panel whose radios to scan.</param>
        /// <param name="brandValue">The brand enum value to match against each radio's <c>Tag</c>.</param>
        private static void CheckBrandRadio(Panel panel, object brandValue)
        {
            foreach (var child in panel.Children)
            {
                if (child is RadioButton rb)
                {
                    rb.IsChecked = Equals(rb.Tag, brandValue);
                }
            }
        }

        /// <summary>
        /// Re-applies the opacity to the preview. The WinForms <c>_original</c> bitmap reset is dropped —
        /// there is no cached bitmap because opacity is now a render property.
        /// </summary>
        private void ResetImage()
        {
            UpdateOpacity();
        }

        /// <summary>
        /// [XPLAT] Applies the configured opacity to the preview image and updates the percent readout.
        /// Replaces the WinForms GDI <c>SetAlpha</c>/<c>ColorMatrix</c> blend with
        /// <see cref="Visual.Opacity"/>.
        /// </summary>
        private void UpdateOpacity()
        {
            previewImage.Opacity = _vehicleConfig.Opacity; // [XPLAT] render opacity replaces ColorMatrix
            // [XPLAT] InvariantCulture so the percent text never drifts with the host OS locale.
            lblOpacityPercent.Text = ((int)(_vehicleConfig.Opacity * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Switches the active vehicle type to Tractor and re-renders. Null-guarded like the source.</summary>
        private void rbtnTractor_Click(object sender, RoutedEventArgs e)
        {
            if (_vehicleConfig == null) return;
            _vehicleConfig.Type = VehicleType.Tractor;
            VehicleSettings.Default.setVehicle_vehicleType = 0;
            UpdateImage();
        }

        /// <summary>Switches the active vehicle type to Harvester and re-renders. Null-guarded like the source.</summary>
        private void rbtnHarvester_Click(object sender, RoutedEventArgs e)
        {
            if (_vehicleConfig == null) return;
            _vehicleConfig.Type = VehicleType.Harvester;
            VehicleSettings.Default.setVehicle_vehicleType = 1;
            UpdateImage();
        }

        /// <summary>Switches the active vehicle type to Articulated and re-renders. Null-guarded like the source.</summary>
        private void rbtnArticulated_Click(object sender, RoutedEventArgs e)
        {
            if (_vehicleConfig == null) return;
            _vehicleConfig.Type = VehicleType.Articulated;
            VehicleSettings.Default.setVehicle_vehicleType = 2;
            UpdateImage();
        }

        /// <summary>
        /// Handles selection of a tractor brand radio. Guarded on <c>IsChecked == true</c> so only the
        /// newly-checked radio acts (the group also raises the event for the radio being unchecked).
        /// </summary>
        private void OnTractorBrandChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                TractorBrand = (TractorBrand)rb.Tag;
                ResetImage();
            }
        }

        /// <summary>Handles selection of a harvester brand radio. Guarded on <c>IsChecked == true</c>.</summary>
        private void OnHarvesterBrandChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                HarvesterBrand = (HarvesterBrand)rb.Tag;
                ResetImage();
            }
        }

        /// <summary>Handles selection of an articulated brand radio. Guarded on <c>IsChecked == true</c>.</summary>
        private void OnArticulatedBrandChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true)
            {
                ArticulatedBrand = (ArticulatedBrand)rb.Tag;
                ResetImage();
            }
        }

        /// <summary>
        /// Toggles whether the vehicle is drawn as an image. [XPLAT] A checked box means "No Image", so the
        /// config flag is the inverse of the checkbox state. Persists the choice and re-renders.
        /// </summary>
        private void cboxIsImage_Click(object sender, RoutedEventArgs e)
        {
            // [XPLAT] checked == "No Image"; IsChecked is bool? so coalesce to false.
            _vehicleConfig.IsImage = !(cboxIsImage.IsChecked ?? false);

            AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity = (int)(_vehicleConfig.Opacity * 100);
            AgOpenGPS.Properties.Settings.Default.setDisplay_isVehicleImage = _vehicleConfig.IsImage;
            AgOpenGPS.Properties.Settings.Default.Save();

            UpdateImage();
        }

        /// <summary>Steps the opacity down by 0.2 (clamped to a 0.2 floor), persists it, and re-applies.</summary>
        private void btnOpacityDn_Click(object sender, RoutedEventArgs e)
        {
            _vehicleConfig.Opacity = Math.Max(_vehicleConfig.Opacity - 0.2, 0.2);
            AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity = (int)(_vehicleConfig.Opacity * 100);
            AgOpenGPS.Properties.Settings.Default.Save();
            UpdateOpacity();
        }

        /// <summary>Steps the opacity up by 0.2 (clamped to a 1.0 ceiling), persists it, and re-applies.</summary>
        private void btnOpacityUp_Click(object sender, RoutedEventArgs e)
        {
            _vehicleConfig.Opacity = Math.Min(_vehicleConfig.Opacity + 0.2, 1.0);
            AgOpenGPS.Properties.Settings.Default.setDisplay_vehicleOpacity = (int)(_vehicleConfig.Opacity * 100);
            AgOpenGPS.Properties.Settings.Default.Save();
            UpdateOpacity();
        }

        /// <summary>
        /// [XPLAT] Builds the per-brand selector radios into their panels. The order matches the WinForms
        /// <c>Controls.Add</c> order so the on-screen layout matches; each panel is its own radio group via
        /// a distinct <c>GroupName</c>. Logos are loaded from packaged <c>avares://</c> brand assets.
        /// </summary>
        private void BuildBrandRadios()
        {
            // panelTractorBrands — GroupName "TractorBrands" (14 brands). Note: TractorBrand.AGOpenGPS has a
            // capital "G" (distinct from HarvesterBrand.AgOpenGPS / ArticulatedBrand.AgOpenGPS).
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.JCB, "BrandJCB", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.AGOpenGPS, "BrandAoG", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Case, "BrandCase", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Claas, "BrandClaas", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Deutz, "BrandDeutz", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Fendt, "BrandFendt", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.JohnDeere, "BrandJohnDeere", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Kubota, "BrandKubota", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Massey, "BrandMassey", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.NewHolland, "BrandNewHolland", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Same, "BrandSame", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Steyr, "BrandSteyr", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Valtra, "BrandValtra", "TractorBrands", OnTractorBrandChecked));
            panelTractorBrands.Children.Add(MakeBrandRadio(TractorBrand.Ursus, "BrandUrsus", "TractorBrands", OnTractorBrandChecked));

            // panelHarvesterBrands — GroupName "HarvesterBrands" (5 brands).
            panelHarvesterBrands.Children.Add(MakeBrandRadio(HarvesterBrand.AgOpenGPS, "BrandAoG", "HarvesterBrands", OnHarvesterBrandChecked));
            panelHarvesterBrands.Children.Add(MakeBrandRadio(HarvesterBrand.Case, "BrandCase", "HarvesterBrands", OnHarvesterBrandChecked));
            panelHarvesterBrands.Children.Add(MakeBrandRadio(HarvesterBrand.Claas, "BrandClaas", "HarvesterBrands", OnHarvesterBrandChecked));
            panelHarvesterBrands.Children.Add(MakeBrandRadio(HarvesterBrand.JohnDeere, "BrandJohnDeere", "HarvesterBrands", OnHarvesterBrandChecked));
            panelHarvesterBrands.Children.Add(MakeBrandRadio(HarvesterBrand.NewHolland, "BrandNewHolland", "HarvesterBrands", OnHarvesterBrandChecked));

            // panelArticulatedBrands — GroupName "ArticulatedBrands" (6 brands).
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.Holder, "BrandHolder", "ArticulatedBrands", OnArticulatedBrandChecked));
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.AgOpenGPS, "BrandAoG", "ArticulatedBrands", OnArticulatedBrandChecked));
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.Challenger, "BrandChallenger", "ArticulatedBrands", OnArticulatedBrandChecked));
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.Case, "BrandCase", "ArticulatedBrands", OnArticulatedBrandChecked));
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.NewHolland, "BrandNewHolland", "ArticulatedBrands", OnArticulatedBrandChecked));
            panelArticulatedBrands.Children.Add(MakeBrandRadio(ArticulatedBrand.JohnDeere, "BrandJohnDeere", "ArticulatedBrands", OnArticulatedBrandChecked));
        }

        /// <summary>
        /// [XPLAT] Builds one flat, image-only brand radio (styled by the paired XAML's
        /// <c>RadioButton.brand</c> class). The radio carries the brand enum in its <c>Tag</c> for
        /// matching, belongs to <paramref name="groupName"/> for mutual exclusivity, and raises
        /// <paramref name="checkedHandler"/> on selection.
        /// </summary>
        /// <param name="brandValue">The brand enum value carried in the radio's <c>Tag</c>.</param>
        /// <param name="logoName">The avares brand-logo file stem (e.g. "BrandJCB").</param>
        /// <param name="groupName">The radio group the button belongs to (one group per panel).</param>
        /// <param name="checkedHandler">The selection handler to subscribe to <c>IsCheckedChanged</c>.</param>
        /// <returns>The configured <see cref="RadioButton"/> (the caller adds it to its panel).</returns>
        private static RadioButton MakeBrandRadio(object brandValue, string logoName, string groupName, EventHandler<RoutedEventArgs> checkedHandler)
        {
            var radioButton = new RadioButton
            {
                GroupName = groupName,
                Tag = brandValue,
                Content = new Image { Source = LoadBrandLogo(logoName), Stretch = Stretch.Uniform },
            };
            radioButton.Classes.Add("brand"); // [XPLAT] applies the XAML RadioButton.brand style (64x64 flat image)
            radioButton.IsCheckedChanged += checkedHandler;
            return radioButton;
        }

        /// <summary>
        /// [XPLAT] Loads a brand logo from the GPS assembly's packaged <c>avares://</c> assets (replaces the
        /// WinForms embedded <c>BrandImages.*</c> resources).
        /// </summary>
        /// <param name="logoName">The brand-logo file stem under <c>ResourcesBrands/Brands/Brand</c>.</param>
        /// <returns>An Avalonia <see cref="IImage"/> for the requested logo.</returns>
        private static IImage LoadBrandLogo(string logoName)
        {
            return new Bitmap(AssetLoader.Open(new Uri($"avares://AgOpenGPS/ResourcesBrands/Brands/Brand/{logoName}.png")));
        }
    }
}
