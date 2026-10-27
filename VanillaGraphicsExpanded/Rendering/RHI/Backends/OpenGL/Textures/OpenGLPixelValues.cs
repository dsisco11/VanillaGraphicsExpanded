using System;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Translates pixel layout values explicitly and computes client-memory bounds.</summary>
internal static class OpenGLPixelValues
{
    #region Internal API
    /// <summary>Maps client component ordering to the native transfer representation.</summary>
    internal static Native.PixelFormat Channels(PixelChannelLayout value) => value switch
    {
        PixelChannelLayout.Red => Native.PixelFormat.Red,
        PixelChannelLayout.Rg => Native.PixelFormat.Rg,
        PixelChannelLayout.Rgb => Native.PixelFormat.Rgb,
        PixelChannelLayout.Rgba => Native.PixelFormat.Rgba,
        PixelChannelLayout.Bgr => Native.PixelFormat.Bgr,
        PixelChannelLayout.Bgra => Native.PixelFormat.Bgra,
        PixelChannelLayout.RedInteger => Native.PixelFormat.RedInteger,
        PixelChannelLayout.RgInteger => Native.PixelFormat.RgInteger,
        PixelChannelLayout.RgbInteger => Native.PixelFormat.RgbInteger,
        PixelChannelLayout.RgbaInteger => Native.PixelFormat.RgbaInteger,
        PixelChannelLayout.Depth => Native.PixelFormat.DepthComponent,
        PixelChannelLayout.Stencil => Native.PixelFormat.StencilIndex,
        PixelChannelLayout.DepthStencil => Native.PixelFormat.DepthStencil,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Maps scalar/packed encoding independently of semantic enum values.</summary>
    internal static Native.PixelType Type(PixelComponentType value) => value switch
    {
        PixelComponentType.Byte => Native.PixelType.Byte,
        PixelComponentType.UnsignedByte => Native.PixelType.UnsignedByte,
        PixelComponentType.Short => Native.PixelType.Short,
        PixelComponentType.UnsignedShort => Native.PixelType.UnsignedShort,
        PixelComponentType.Int => Native.PixelType.Int,
        PixelComponentType.UnsignedInt => Native.PixelType.UnsignedInt,
        PixelComponentType.Half => Native.PixelType.HalfFloat,
        PixelComponentType.Float => Native.PixelType.Float,
        PixelComponentType.UnsignedInt248 => Native.PixelType.UnsignedInt248,
        PixelComponentType.Float32UnsignedInt248 => Native.PixelType.Float32UnsignedInt248Rev,
        PixelComponentType.UnsignedInt2101010 => Native.PixelType.UnsignedInt2101010Rev,
        PixelComponentType.UnsignedInt10F11F11F => Native.PixelType.UnsignedInt10F11F11FRev,
        PixelComponentType.UnsignedInt5999 => Native.PixelType.UnsignedInt5999Rev,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
    /// <summary>Computes bytes per pixel while rejecting mismatched packed encodings.</summary>
    internal static int PixelBytes(RHIPixelEncoding encoding)
    {
        _ = Channels(encoding.Channels);
        _ = Type(encoding.Type);
        int components = encoding.Channels switch
        {
            PixelChannelLayout.Red or PixelChannelLayout.RedInteger or PixelChannelLayout.Depth or PixelChannelLayout.Stencil => 1,
            PixelChannelLayout.Rg or PixelChannelLayout.RgInteger => 2,
            PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger or PixelChannelLayout.Bgr => 3,
            PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger or PixelChannelLayout.Bgra => 4,
            _ => 0
        };
        if (encoding.Type is PixelComponentType.UnsignedInt248 or PixelComponentType.Float32UnsignedInt248)
        {
            if (encoding.Channels != PixelChannelLayout.DepthStencil) throw new ArgumentException("Packed depth/stencil requires both components.");
            return encoding.Type == PixelComponentType.Float32UnsignedInt248 ? 8 : 4;
        }
        if (components == 0) throw new ArgumentException("Depth/stencil requires a packed representation.");
        if (encoding.Type is PixelComponentType.UnsignedInt2101010 or PixelComponentType.UnsignedInt10F11F11F or PixelComponentType.UnsignedInt5999)
        {
            if (components != (encoding.Type == PixelComponentType.UnsignedInt2101010 ? 4 : 3)) throw new ArgumentException("Packed pixel component count is incompatible.");
            return 4;
        }
        return components * (encoding.Type switch { PixelComponentType.Byte or PixelComponentType.UnsignedByte => 1, PixelComponentType.Short or PixelComponentType.UnsignedShort or PixelComponentType.Half => 2, _ => 4 });
    }
    /// <summary>Translates existing wrapper pixel policy into semantic client encoding.</summary>
    internal static RHIPixelEncoding Encoding(OpenTK.Graphics.OpenGL.PixelFormat channels, OpenTK.Graphics.OpenGL.PixelType type) => new(
        channels switch
        {
            OpenTK.Graphics.OpenGL.PixelFormat.Red => PixelChannelLayout.Red,
            OpenTK.Graphics.OpenGL.PixelFormat.Rg => PixelChannelLayout.Rg,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgb => PixelChannelLayout.Rgb,
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba => PixelChannelLayout.Rgba,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgr => PixelChannelLayout.Bgr,
            OpenTK.Graphics.OpenGL.PixelFormat.Bgra => PixelChannelLayout.Bgra,
            OpenTK.Graphics.OpenGL.PixelFormat.RedInteger => PixelChannelLayout.RedInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgInteger => PixelChannelLayout.RgInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgbInteger => PixelChannelLayout.RgbInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.RgbaInteger => PixelChannelLayout.RgbaInteger,
            OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent => PixelChannelLayout.Depth,
            OpenTK.Graphics.OpenGL.PixelFormat.StencilIndex => PixelChannelLayout.Stencil,
            OpenTK.Graphics.OpenGL.PixelFormat.DepthStencil => PixelChannelLayout.DepthStencil,
            _ => throw new ArgumentOutOfRangeException(nameof(channels))
        }, type switch
        {
            OpenTK.Graphics.OpenGL.PixelType.Byte => PixelComponentType.Byte,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedByte => PixelComponentType.UnsignedByte,
            OpenTK.Graphics.OpenGL.PixelType.Short => PixelComponentType.Short,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedShort => PixelComponentType.UnsignedShort,
            OpenTK.Graphics.OpenGL.PixelType.Int => PixelComponentType.Int,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt => PixelComponentType.UnsignedInt,
            OpenTK.Graphics.OpenGL.PixelType.HalfFloat => PixelComponentType.Half,
            OpenTK.Graphics.OpenGL.PixelType.Float => PixelComponentType.Float,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt248 => PixelComponentType.UnsignedInt248,
            OpenTK.Graphics.OpenGL.PixelType.Float32UnsignedInt248Rev => PixelComponentType.Float32UnsignedInt248,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt2101010Rev => PixelComponentType.UnsignedInt2101010,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt10F11F11FRev => PixelComponentType.UnsignedInt10F11F11F,
            OpenTK.Graphics.OpenGL.PixelType.UnsignedInt5999Rev => PixelComponentType.UnsignedInt5999,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        });
    #endregion
}
