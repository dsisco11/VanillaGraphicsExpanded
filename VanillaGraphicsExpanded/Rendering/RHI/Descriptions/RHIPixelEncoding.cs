using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Defines semantic client pixel interpretation and checked byte size without native discovery.</summary>
public readonly record struct RHIPixelEncoding(PixelChannelLayout Channels, PixelComponentType Type)
{
    #region Public API
    /// <summary>Gets the packed/scalar pixel byte count, rejecting incompatible component layouts.</summary>
    public int BytesPerPixel
    {
        get
        {
            if (!Enum.IsDefined(Channels) || !Enum.IsDefined(Type)) throw new ArgumentException("Unknown pixel encoding.");
            int components = Channels switch
            {
                PixelChannelLayout.Red or PixelChannelLayout.RedInteger or PixelChannelLayout.Depth or PixelChannelLayout.Stencil => 1,
                PixelChannelLayout.Rg or PixelChannelLayout.RgInteger => 2,
                PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger or PixelChannelLayout.Bgr => 3,
                PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger or PixelChannelLayout.Bgra => 4,
                _ => 0
            };
            if (Type is PixelComponentType.UnsignedInt248 or PixelComponentType.Float32UnsignedInt248)
            {
                if (Channels != PixelChannelLayout.DepthStencil) throw new ArgumentException("Packed depth/stencil requires both components.");
                return Type == PixelComponentType.Float32UnsignedInt248 ? 8 : 4;
            }
            if (components == 0) throw new ArgumentException("Depth/stencil requires a packed representation.");
            if (Type is PixelComponentType.UnsignedInt2101010 or PixelComponentType.UnsignedInt10F11F11F or PixelComponentType.UnsignedInt5999)
            {
                if (components != (Type == PixelComponentType.UnsignedInt2101010 ? 4 : 3)) throw new ArgumentException("Packed pixel component count is incompatible.");
                return 4;
            }
            return components * (Type switch { PixelComponentType.Byte or PixelComponentType.UnsignedByte => 1, PixelComponentType.Short or PixelComponentType.UnsignedShort or PixelComponentType.Half => 2, _ => 4 });
        }
    }
    #endregion
}
