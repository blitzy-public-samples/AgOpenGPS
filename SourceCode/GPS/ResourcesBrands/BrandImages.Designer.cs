// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
//
// Hand-written cross-platform replacement for the former resx-generated
// StronglyTypedResourceBuilder class (BrandImages.resx retired). The 48 brand PNG
// assets are byte-identical to the WinForms baseline; only the loading mechanism
// changes. They are packaged as <AvaloniaResource Include="ResourcesBrands\**\*.png" />
// in AgOpenGPS.csproj and resolved at runtime against the AgOpenGPS assembly via
// avares:// URIs as Avalonia.Media.Imaging.Bitmap — because System.Drawing.Bitmap /
// System.Drawing.Common is Windows-only on net8 and cannot remain in a tri-platform
// (Windows/Linux/macOS) build.
//
// The public contract is preserved exactly so existing call sites compile unchanged:
//   * namespace  AgOpenGPS.ResourcesBrands
//   * type        BrandImages
//   * 48 members  same names, same `internal static` access — return type only changes
//                 from System.Drawing.Bitmap to Avalonia.Media.Imaging.Bitmap.
//
// NOTE: this file keeps its historical *.Designer.cs name to minimise churn, but it is
// now HAND-MAINTAINED — there is no .resx generator behind it. To add or rename a brand
// image, drop the PNG under ResourcesBrands/Brands/<Group>/ and add a matching member
// below (the member name equals the PNG file name; the group equals the sub-folder).
using System;
using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AgOpenGPS.ResourcesBrands
{
    /// <summary>
    /// Strongly-typed, cross-platform accessor for the embedded vehicle/brand imagery used by
    /// the vehicle-configuration UI (via <c>Classes/Brands.cs</c>). Each member returns the
    /// decoded <see cref="Bitmap"/> for one brand PNG, loaded from an
    /// <c>avares://AgOpenGPS/ResourcesBrands/Brands/&lt;Group&gt;/&lt;Name&gt;.png</c> resource.
    /// Images are immutable, shared for the lifetime of the application, and decoded at most once.
    /// </summary>
    internal static class BrandImages
    {
        /// <summary>
        /// Base <c>avares://</c> URI for every brand asset. The leading authority segment is the
        /// owning assembly name (<c>AgOpenGPS</c>); the remainder mirrors the on-disk project path
        /// <c>ResourcesBrands/Brands/</c> against which the <c>AvaloniaResource</c> glob is rooted.
        /// </summary>
        private const string BaseUri = "avares://AgOpenGPS/ResourcesBrands/Brands/";

        /// <summary>
        /// Process-lifetime cache keyed by the group-relative resource path (for example
        /// <c>"Tractor/TractorCase"</c>). Brand images are static and are never mutated or disposed
        /// by callers, so a single shared <see cref="Bitmap"/> instance per asset is both safe and
        /// efficient. <see cref="ConcurrentDictionary{TKey,TValue}"/> guards against the UI thread
        /// and any background view-model resolving the same image concurrently.
        /// </summary>
        private static readonly ConcurrentDictionary<string, Bitmap> _cache =
            new ConcurrentDictionary<string, Bitmap>();

        /// <summary>
        /// Decodes the brand PNG identified by <paramref name="relativePath"/> (a group-relative
        /// path without extension, for example <c>"Brand/BrandTriangleVehicle"</c>), caching the
        /// result so each asset is opened and decoded only once. The asset stream is fully consumed
        /// by the <see cref="Bitmap"/> constructor, so it does not need to outlive this call.
        /// </summary>
        /// <param name="relativePath">Group-relative resource path, without the <c>.png</c> suffix.</param>
        /// <returns>The shared, decoded <see cref="Bitmap"/> for the requested brand asset.</returns>
        private static Bitmap Load(string relativePath) =>
            _cache.GetOrAdd(relativePath, path =>
            {
                using (var stream = AssetLoader.Open(new Uri(BaseUri + path + ".png")))
                {
                    return new Bitmap(stream);
                }
            });

        // ---- Articulated tractor front/rear silhouettes (12) ----
        internal static Bitmap ArticulatedFrontAoG => Load("Articulated/ArticulatedFrontAoG");
        internal static Bitmap ArticulatedFrontCase => Load("Articulated/ArticulatedFrontCase");
        internal static Bitmap ArticulatedFrontChallenger => Load("Articulated/ArticulatedFrontChallenger");
        internal static Bitmap ArticulatedFrontHolder => Load("Articulated/ArticulatedFrontHolder");
        internal static Bitmap ArticulatedFrontJohnDeere => Load("Articulated/ArticulatedFrontJohnDeere");
        internal static Bitmap ArticulatedFrontNewHolland => Load("Articulated/ArticulatedFrontNewHolland");
        internal static Bitmap ArticulatedRearAoG => Load("Articulated/ArticulatedRearAoG");
        internal static Bitmap ArticulatedRearCase => Load("Articulated/ArticulatedRearCase");
        internal static Bitmap ArticulatedRearChallenger => Load("Articulated/ArticulatedRearChallenger");
        internal static Bitmap ArticulatedRearHolder => Load("Articulated/ArticulatedRearHolder");
        internal static Bitmap ArticulatedRearJohnDeere => Load("Articulated/ArticulatedRearJohnDeere");
        internal static Bitmap ArticulatedRearNewHolland => Load("Articulated/ArticulatedRearNewHolland");

        // ---- Brand logos (17) ----
        internal static Bitmap BrandAoG => Load("Brand/BrandAoG");
        internal static Bitmap BrandCase => Load("Brand/BrandCase");
        internal static Bitmap BrandChallenger => Load("Brand/BrandChallenger");
        internal static Bitmap BrandClaas => Load("Brand/BrandClaas");
        internal static Bitmap BrandDeutz => Load("Brand/BrandDeutz");
        internal static Bitmap BrandFendt => Load("Brand/BrandFendt");
        internal static Bitmap BrandHolder => Load("Brand/BrandHolder");
        internal static Bitmap BrandJCB => Load("Brand/BrandJCB");
        internal static Bitmap BrandJohnDeere => Load("Brand/BrandJohnDeere");
        internal static Bitmap BrandKubota => Load("Brand/BrandKubota");
        internal static Bitmap BrandMassey => Load("Brand/BrandMassey");
        internal static Bitmap BrandNewHolland => Load("Brand/BrandNewHolland");
        internal static Bitmap BrandSame => Load("Brand/BrandSame");
        internal static Bitmap BrandSteyr => Load("Brand/BrandSteyr");
        internal static Bitmap BrandTriangleVehicle => Load("Brand/BrandTriangleVehicle");
        internal static Bitmap BrandUrsus => Load("Brand/BrandUrsus");
        internal static Bitmap BrandValtra => Load("Brand/BrandValtra");

        // ---- Harvester silhouettes (5) ----
        internal static Bitmap HarvesterAoG => Load("Harvester/HarvesterAoG");
        internal static Bitmap HarvesterCase => Load("Harvester/HarvesterCase");
        internal static Bitmap HarvesterClaas => Load("Harvester/HarvesterClaas");
        internal static Bitmap HarvesterJohnDeere => Load("Harvester/HarvesterJohnDeere");
        internal static Bitmap HarvesterNewHolland => Load("Harvester/HarvesterNewHolland");

        // ---- Tractor silhouettes (14) ----
        internal static Bitmap TractorAoG => Load("Tractor/TractorAoG");
        internal static Bitmap TractorCase => Load("Tractor/TractorCase");
        internal static Bitmap TractorClaas => Load("Tractor/TractorClaas");
        internal static Bitmap TractorDeutz => Load("Tractor/TractorDeutz");
        internal static Bitmap TractorFendt => Load("Tractor/TractorFendt");
        internal static Bitmap TractorJCB => Load("Tractor/TractorJCB");
        internal static Bitmap TractorJohnDeere => Load("Tractor/TractorJohnDeere");
        internal static Bitmap TractorKubota => Load("Tractor/TractorKubota");
        internal static Bitmap TractorMassey => Load("Tractor/TractorMassey");
        internal static Bitmap TractorNewHolland => Load("Tractor/TractorNewHolland");
        internal static Bitmap TractorSame => Load("Tractor/TractorSame");
        internal static Bitmap TractorSteyr => Load("Tractor/TractorSteyr");
        internal static Bitmap TractorUrsus => Load("Tractor/TractorUrsus");
        internal static Bitmap TractorValtra => Load("Tractor/TractorValtra");
    }
}
