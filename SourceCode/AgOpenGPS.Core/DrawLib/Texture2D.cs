// [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
using System;
using System.Runtime.InteropServices;
using AgOpenGPS.Core.Models;
using OpenTK.Graphics.OpenGL;

namespace AgOpenGPS.Core.DrawLib
{
    public class Texture2D : IDisposable
    {
        // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
        // Portable RGBA pixel source replaces the former GDI+ image backing
        // (GDI+ imaging is Windows-only on net8/9 and throws at runtime on Linux/macOS).
        // _pixels: tightly-packed RGBA (R,G,B,A), 4 bytes/pixel, width*height*4 bytes, row-major; MAY be null (deferred).
        private readonly byte[] _pixels;
        private readonly int _width;
        private readonly int _height;
        private int _textureId;
        private bool isDisposed;

        // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
        // rgbaPixels: tightly-packed RGBA (R,G,B,A), 4 bytes/pixel, width*height*4 bytes, row-major.
        // rgbaPixels MAY be null to create a deferred/empty texture that is filled later via SetPixels(...).
        public Texture2D(byte[] rgbaPixels, int width, int height)
        {
            _pixels = rgbaPixels;
            _width = width;
            _height = height;
            // To avoid crashes during start-up (when no OpenGL context exists yet),
            // delay creation of the GL texture until the first call to Bind().
        }

        public void Bind()
        {
            if (0 == _textureId) CreateTexture();
            GL.BindTexture(TextureTarget.Texture2D, _textureId);
        }

        public void Draw(
            XyCoord u0v0, // The corner (u==0.0 && v==0.0) of the texture will be mapped to this coord
            XyCoord u1v1  // The corner (u==1.0 && v==1.0) of the texture will be mapped to this coord
        )
        {
            GL.Enable(EnableCap.Texture2D);

            Bind();
            GL.Begin(PrimitiveType.Quads);
            GL.TexCoord2(0, 0); GL.Vertex2(u0v0.X, u0v0.Y);
            GL.TexCoord2(1, 0); GL.Vertex2(u1v1.X, u0v0.Y);
            GL.TexCoord2(1, 1); GL.Vertex2(u1v1.X, u1v1.Y);
            GL.TexCoord2(0, 1); GL.Vertex2(u0v0.X, u1v1.Y);
            GL.End();
            GL.Disable(EnableCap.Texture2D);
        }

        public void DrawCenteredAroundOrigin(
            XyDelta centerToU1V1)
        {
            XyCoord origin = new XyCoord(0.0, 0.0);
            Draw(origin - centerToU1V1, origin + centerToU1V1);
        }

        public void DrawCentered(
            XyCoord center,      // The center of the texture will be mapped to this coord
            XyDelta centerToU1V1 // Typically 0.5 * the size. Negative values for X and/or Y
                                 // will flip the corresponding U and/or V axis of the texture.
        )
        {
            Draw(center - centerToU1V1, center + centerToU1V1);
        }

        // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
        // Uploads a tightly-packed RGBA pixel buffer (4 bytes/pixel, row-major) as the texture image.
        // Replaces the former GDI+ image-upload method: the locked-pixel path is gone and the GL source
        // format flips from .Bgra (GDI+ ARGB-in-memory) to .Rgba (portable RGBA bytes).
        public void SetPixels(byte[] rgbaPixels, int width, int height)
        {
            Bind();
            GCHandle handle = GCHandle.Alloc(rgbaPixels, GCHandleType.Pinned);
            try
            {
                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    width,
                    height,
                    0,
                    OpenTK.Graphics.OpenGL.PixelFormat.Rgba,   // [XPLAT] was .Bgra (GDI+ ARGB-in-memory); now RGBA source bytes
                    PixelType.UnsignedByte,
                    handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, 9729); // GL_LINEAR — preserve EXACTLY
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, 9729); // GL_LINEAR — preserve EXACTLY
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        private void CreateTexture()
        {
            _textureId = GL.GenTexture();
            // [XPLAT] migrated from net48/WinForms — see MIGRATION_DOCS/TRANSITION_MAP.md
            // Upload the constructor-provided pixels only when present (mirrors the former deferred-image guard).
            if (_pixels != null) SetPixels(_pixels, _width, _height);
        }

        private void DeleteTexture()
        {
            if (0 != _textureId)
            {
                GL.DeleteTexture(_textureId);
            }
            _textureId = 0;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                if (disposing)
                {
                    // Only delete texture when explicitly disposed, not during finalization
                    // OpenGL calls require an active context and must be on the correct thread
                    DeleteTexture();
                }
                isDisposed = true;
            }
        }

        ~Texture2D()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
