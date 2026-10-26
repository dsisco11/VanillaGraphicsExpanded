using System;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates pixel layout values explicitly and computes client-memory bounds.</summary>
internal static class OpenGLPixelValues
{
    #region Internal API
    /// <summary>Maps client component ordering to the native transfer representation.</summary>
    internal static Native.PixelFormat Channels(RHIPixelChannels value) => value switch
    {
        RHIPixelChannels.Red => Native.PixelFormat.Red,
        RHIPixelChannels.Rg => Native.PixelFormat.Rg,
        RHIPixelChannels.Rgb => Native.PixelFormat.Rgb,
        RHIPixelChannels.Rgba => Native.PixelFormat.Rgba,
        RHIPixelChannels.Bgr => Native.PixelFormat.Bgr,
        RHIPixelChannels.Bgra => Native.PixelFormat.Bgra,
        RHIPixelChannels.RedInteger => Native.PixelFormat.RedInteger,
        RHIPixelChannels.RgInteger => Native.PixelFormat.RgInteger,
        RHIPixelChannels.RgbInteger => Native.PixelFormat.RgbInteger,
        RHIPixelChannels.RgbaInteger => Native.PixelFormat.RgbaInteger,
        RHIPixelChannels.Depth => Native.PixelFormat.DepthComponent,
        RHIPixelChannels.Stencil => Native.PixelFormat.StencilIndex,
        RHIPixelChannels.DepthStencil => Native.PixelFormat.DepthStencil,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps scalar/packed encoding independently of semantic enum values.</summary>
    internal static Native.PixelType Type(RHIPixelType value) => value switch
    {
        RHIPixelType.Byte => Native.PixelType.Byte,
        RHIPixelType.UnsignedByte => Native.PixelType.UnsignedByte,
        RHIPixelType.Short => Native.PixelType.Short,
        RHIPixelType.UnsignedShort => Native.PixelType.UnsignedShort,
        RHIPixelType.Int => Native.PixelType.Int,
        RHIPixelType.UnsignedInt => Native.PixelType.UnsignedInt,
        RHIPixelType.Half => Native.PixelType.HalfFloat,
        RHIPixelType.Float => Native.PixelType.Float,
        RHIPixelType.UnsignedInt248 => Native.PixelType.UnsignedInt248,
        RHIPixelType.Float32UnsignedInt248 => Native.PixelType.Float32UnsignedInt248Rev,
        RHIPixelType.UnsignedInt2101010 => Native.PixelType.UnsignedInt2101010Rev,
        RHIPixelType.UnsignedInt10F11F11F => Native.PixelType.UnsignedInt10F11F11FRev,
        RHIPixelType.UnsignedInt5999 => Native.PixelType.UnsignedInt5999Rev,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Computes bytes per pixel while rejecting mismatched packed encodings.</summary>
    internal static int PixelBytes(RHIPixelEncoding encoding)
    {
        _ = Channels(encoding.Channels);
        _ = Type(encoding.Type);
        int components = encoding.Channels switch
        {
            RHIPixelChannels.Red or RHIPixelChannels.RedInteger or RHIPixelChannels.Depth or RHIPixelChannels.Stencil => 1,
            RHIPixelChannels.Rg or RHIPixelChannels.RgInteger => 2,
            RHIPixelChannels.Rgb or RHIPixelChannels.RgbInteger or RHIPixelChannels.Bgr => 3,
            RHIPixelChannels.Rgba or RHIPixelChannels.RgbaInteger or RHIPixelChannels.Bgra => 4,
            _ => 0
        };
        if (encoding.Type is RHIPixelType.UnsignedInt248 or RHIPixelType.Float32UnsignedInt248)
        {
            if (encoding.Channels != RHIPixelChannels.DepthStencil) throw new ArgumentException("Packed depth/stencil requires both components.");
            return encoding.Type == RHIPixelType.Float32UnsignedInt248 ? 8 : 4;
        }
        if (components == 0) throw new ArgumentException("Depth/stencil requires a packed representation.");
        if (encoding.Type is RHIPixelType.UnsignedInt2101010 or RHIPixelType.UnsignedInt10F11F11F or RHIPixelType.UnsignedInt5999)
        {
            if (components != (encoding.Type == RHIPixelType.UnsignedInt2101010 ? 4 : 3)) throw new ArgumentException("Packed pixel component count is incompatible.");
            return 4;
        }
        return components * (encoding.Type switch { RHIPixelType.Byte or RHIPixelType.UnsignedByte => 1, RHIPixelType.Short or RHIPixelType.UnsignedShort or RHIPixelType.Half => 2, _ => 4 });
    }
    /// <summary>Translates existing wrapper pixel policy into semantic client encoding.</summary>
    internal static RHIPixelEncoding Encoding(OpenTK.Graphics.OpenGL.PixelFormat channels, OpenTK.Graphics.OpenGL.PixelType type) => new(
        channels switch
        {
            OpenTK.Graphics.OpenGL.PixelFormat.Red => RHIPixelChannels.Red,
            OpenTK.Graphics.OpenGL.PixelFormat.Rg => RHIPixelChannels.Rg,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgb => RHIPixelChannels.Rgb,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba => RHIPixelChannels.Rgba,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgr => RHIPixelChannels.Bgr,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgra => RHIPixelChannels.Bgra,
            OpenTK.Graphics.OpenGL.PixelFormat.RedInteger => RHIPixelChannels.RedInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgInteger => RHIPixelChannels.RgInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgbInteger => RHIPixelChannels.RgbInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgbaInteger => RHIPixelChannels.RgbaInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent => RHIPixelChannels.Depth,
            OpenTK.Graphics.OpenGL.PixelFormat.StencilIndex => RHIPixelChannels.Stencil,
            OpenTK.Graphics.OpenGL.PixelFormat.DepthStencil => RHIPixelChannels.DepthStencil,
            _ => throw new ArgumentOutOfRangeException(nameof(channels))
        }, type switch
        {
            OpenTK.Graphics.OpenGL.PixelType.Byte => RHIPixelType.Byte,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedByte => RHIPixelType.UnsignedByte,
            OpenTK.Graphics.OpenGL.PixelType.Short => RHIPixelType.Short,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedShort => RHIPixelType.UnsignedShort,
            OpenTK.Graphics.OpenGL.PixelType.Int => RHIPixelType.Int,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt => RHIPixelType.UnsignedInt,
            OpenTK.Graphics.OpenGL.PixelType.HalfFloat => RHIPixelType.Half,
            OpenTK.Graphics.OpenGL.PixelType.Float => RHIPixelType.Float,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt248 => RHIPixelType.UnsignedInt248,
            OpenTK.Graphics.OpenGL.PixelType.Float32UnsignedInt248Rev => RHIPixelType.Float32UnsignedInt248,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt2101010Rev => RHIPixelType.UnsignedInt2101010,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt10F11F11FRev => RHIPixelType.UnsignedInt10F11F11F,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt5999Rev => RHIPixelType.UnsignedInt5999,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        });
    #endregion
}
