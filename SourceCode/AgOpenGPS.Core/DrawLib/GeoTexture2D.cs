// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using AgOpenGPS.Core.Models;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS.Core.DrawLib
{
    public class GeoTexture2D : Texture2D
    {
        // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
        // The base Texture2D now takes a portable, tightly-packed RGBA byte buffer
        // (4 bytes/pixel, row-major) instead of a Windows-only GDI+ System.Drawing.Bitmap,
        // so this geo-aware subclass forwards the same RGBA contract. rgbaPixels MAY be null
        // to defer the GL texture upload until first Bind() (matches the base behavior).
        public GeoTexture2D(byte[] rgbaPixels, int width, int height) : base(rgbaPixels, width, height)
        {
        }

        public void DrawZ(
            GeoCoord u0v0, // The corner (u==0.0 && v==0.0) of the texture will be mapped to this coord
            GeoCoord u1v1, // The corner (u==1.0 && v==1.0) of the texture will be mapped to this coord
            double zCoord
        )
        {
            GL.Enable(EnableCap.Texture2D);

            Bind();
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0, 0); GL.Vertex3(u0v0.Northing, u0v0.Easting, zCoord);
            GL.TexCoord2(1, 0); GL.Vertex3(u1v1.Northing, u0v0.Easting, zCoord);
            GL.TexCoord2(1, 1); GL.Vertex3(u1v1.Northing, u1v1.Easting, zCoord);
            GL.TexCoord2(0, 1); GL.Vertex3(u0v0.Northing, u1v1.Easting, zCoord);
            GL.End();
            GL.Disable(EnableCap.Texture2D);
        }

        public void DrawRepeatedZ(
            GeoCoord u0v0, // The corner (u==0.0 && v==0.0) of the texture will be mapped to this coord
            GeoCoord u1v1, // The corner (u==1.0 && v==1.0) of the texture will be mapped to this coord
            double zCoord,
            double nRepeat)
        {
            GL.Enable(EnableCap.Texture2D);

            Bind();
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0, 0); GL.Vertex3(u0v0.Northing, u0v0.Easting, zCoord);
            GL.TexCoord2(nRepeat, 0); GL.Vertex3(u1v1.Northing, u0v0.Easting, zCoord);
            GL.TexCoord2(nRepeat, nRepeat); GL.Vertex3(u1v1.Northing, u1v1.Easting, zCoord);
            GL.TexCoord2(0, nRepeat); GL.Vertex3(u0v0.Northing, u1v1.Easting, zCoord);
            GL.End();
            GL.Disable(EnableCap.Texture2D);
        }

    }
}
