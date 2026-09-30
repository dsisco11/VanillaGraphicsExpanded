using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Copies one display framebuffer's BGRA8 pixels through a PBO into a BMP file.</summary>
internal sealed class FramebufferBmpCapture(int width, int height) : IDisposable
{
    private readonly GpuPixelPackBuffer readback = GpuPixelPackBuffer.Create(debugName: "Test.FramebufferBmp.Readback");

    #region Public API
    /// <summary>Reads the framebuffer's first color attachment without altering its pixels.</summary>
    internal void Capture(GpuFramebuffer framebuffer, string path)
    {
        if (framebuffer.Width != width || framebuffer.Height != height)
            throw new ArgumentException("Framebuffer dimensions do not match the capture buffer.", nameof(framebuffer));
        int byteCount = width * height * 4;
        readback.AllocateOrphan(byteCount);
        using var binding = GlStateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, framebuffer.FboId);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        readback.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte);
        IntPtr mapped = readback.MapRange(0, byteCount, MapBufferAccessMask.MapReadBit);
        if (mapped == IntPtr.Zero) throw new InvalidOperationException("Could not map framebuffer readback buffer.");
        byte[] pixels = new byte[byteCount];
        try { Marshal.Copy(mapped, pixels, 0, byteCount); }
        finally { if (!readback.Unmap()) throw new InvalidOperationException("Framebuffer readback buffer was corrupted."); }
        WriteBmp(path, pixels);
    }

    /// <summary>Releases the pixel-pack buffer.</summary>
    public void Dispose() => readback.Dispose();
    #endregion

    #region Private
    /// <summary>Writes the bottom-up BGRA8 readback after a standard 32-bit BMP header.</summary>
    private void WriteBmp(string path, byte[] pixels)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var output = File.Create(fullPath);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0x4d42);
        writer.Write(54 + pixels.Length);
        writer.Write(0);
        writer.Write(54);
        writer.Write(40);
        writer.Write(width);
        writer.Write(height);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(0);
        writer.Write(pixels.Length);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
    }
    #endregion
}
