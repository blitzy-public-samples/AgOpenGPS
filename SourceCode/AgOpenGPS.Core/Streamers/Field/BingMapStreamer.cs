// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgLibrary.Logging;
using AgOpenGPS.Core.Interfaces;
using AgOpenGPS.Core.Models;
using SkiaSharp;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AgOpenGPS.Core.Streamers
{
    public class BingMapStreamer : FieldAspectStreamer
    {
        private readonly BingMapBitmapStreamer _bitmapStreamer;
        public BingMapStreamer() : base("BackPic.txt", null)
        {
            _bitmapStreamer = new BingMapBitmapStreamer();
        }

        public BingMap TryRead(DirectoryInfo fieldDirectory)
        {
            BingMap bingMap = null;
            FileInfo fileInfo = GetFileInfo(fieldDirectory);
            if (fileInfo.Exists)
            {
                try
                {
                    bingMap = Read(fieldDirectory);
                }
                catch (Exception)
                {
                }
            }
            return bingMap;
        }

        public void TryWrite(BingMap bingMap, DirectoryInfo fieldDirectory)
        {
            try
            {
                Write(bingMap, fieldDirectory);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message + "\n Cannot write to file.");
                Log.EventWriter("Saving BingMap" + e.ToString());
            }
        }

        private BingMap Read(DirectoryInfo fieldDirectory)
        {
            BingMap bingMap = null;
            FileInfo fileInfo = GetFileInfo(fieldDirectory);
            using (GeoStreamReader reader = new GeoStreamReader(fileInfo))
            {
                string line = reader.ReadLine(); // skip header
                bool hasBingMap = reader.ReadBool();
                if (hasBingMap)
                {
                    GeoBoundingBox geoBb = reader.ReadGeoBoundingBox();
                    // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
                    // Decode BackPic.png to a portable RGBA buffer via SkiaSharp (was the Windows-only GDI+ raster path).
                    byte[] rgbaPixels = _bitmapStreamer.Read(fieldDirectory, out int width, out int height);
                    if (rgbaPixels != null)
                    {
                        bingMap = new BingMap(geoBb, rgbaPixels, width, height);
                    }
                }
            }
            return bingMap;
        }

        private void Write(BingMap bingMap, DirectoryInfo fieldDirectory)
        {
            if (bingMap != null)
            {
                FileInfo boundingBoxFileInfo = GetFileInfo(fieldDirectory);
                using (GeoStreamWriter writer = new GeoStreamWriter(boundingBoxFileInfo))
                {
                    writer.WriteLine("$BackPic");
                    writer.WriteBool(true);
                    writer.WriteGeoBoundingBox(bingMap.GeoBoundingBox);
                }
                // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
                // Encode the portable RGBA buffer back to BackPic.png via SkiaSharp (was GDI+ Save).
                _bitmapStreamer.Write(bingMap.RgbaPixels, bingMap.Width, bingMap.Height, fieldDirectory);
            }
            else
            {
                DeleteFile(fieldDirectory);
                _bitmapStreamer.DeleteFile(fieldDirectory);
            }
        }

        public void CreateFile(DirectoryInfo fieldDirectory)
        {
            fieldDirectory.Create();
            // [XPLAT] NewLine pin for consistency (no functional effect — file is created empty)
            using (StreamWriter writer = new StreamWriter(GetFileInfo(fieldDirectory).Name) { NewLine = "\r\n" })
            {
            }
        }

        private class BingMapBitmapStreamer : FieldAspectStreamer
        {
            public BingMapBitmapStreamer() : base("BackPic.png", null)
            {
            }

            // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
            // Decodes BackPic.png into a tightly-packed RGBA buffer (4 bytes/pixel, row-major)
            // using the cross-platform SkiaSharp codec, replacing the Windows-only GDI+ raster
            // APIs, which throw at runtime on Linux/macOS. The BackPic.png on-disk format is
            // unchanged, preserving the frozen field-file contract.
            // Returns null (with zero dimensions) when the file is absent or cannot be decoded.
            public byte[] Read(DirectoryInfo fieldDirectory, out int width, out int height)
            {
                width = 0;
                height = 0;
                FileInfo fileInfo = GetFileInfo(fieldDirectory);
                if (!fileInfo.Exists)
                {
                    return null;
                }

                using (FileStream stream = File.OpenRead(fileInfo.FullName))
                using (SKBitmap decoded = SKBitmap.Decode(stream))
                {
                    if (decoded == null)
                    {
                        return null;
                    }

                    // Normalize to RGBA8888 / unpremultiplied so the byte order matches the GL upload
                    // path in Texture2D.SetPixels (PixelFormat.Rgba). BackPic tiles are opaque, so the
                    // copy below is loss-free.
                    SKImageInfo info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    using (SKBitmap rgba = new SKBitmap(info))
                    {
                        using (SKCanvas canvas = new SKCanvas(rgba))
                        {
                            canvas.Clear(SKColors.Transparent);
                            canvas.DrawBitmap(decoded, 0, 0);
                        }
                        width = info.Width;
                        height = info.Height;
                        return rgba.Bytes;
                    }
                }
            }

            // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
            // Encodes a tightly-packed RGBA buffer back to BackPic.png using SkiaSharp (was the
            // Windows-only GDI+ raster-save path). The PNG output remains a standard PNG, preserving
            // the field-file format. No-ops when there is no imagery to write.
            public void Write(byte[] rgbaPixels, int width, int height, DirectoryInfo fieldDirectory)
            {
                FileInfo fileInfo = GetFileInfo(fieldDirectory);
                if (fileInfo.Exists)
                {
                    fileInfo.Delete();
                }
                if (rgbaPixels == null || width <= 0 || height <= 0)
                {
                    return;
                }

                SKImageInfo info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                using (SKBitmap bitmap = new SKBitmap(info))
                {
                    // Copy the managed RGBA bytes into the Skia-allocated pixel buffer.
                    Marshal.Copy(rgbaPixels, 0, bitmap.GetPixels(), rgbaPixels.Length);
                    using (SKImage image = SKImage.FromBitmap(bitmap))
                    using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (FileStream output = File.Create(fileInfo.FullName))
                    {
                        data.SaveTo(output);
                    }
                }
            }
        }
    }
}
