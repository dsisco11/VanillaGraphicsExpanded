using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Distinguishes a whole backing store from an explicitly selected byte range.</summary>
internal enum GpuBufferTextureMode { Whole, Range }

/// <summary>Records explicit legal buffer-texture creation inputs with automatic assignment tracking.</summary>
internal struct GpuBufferTextureInitialization
{
    private byte assigned;
    private int buffer;
    private TextureFormat format;
    private GpuBufferTextureMode mode;
    private nint offset, size;

    #region Public API
    /// <summary>Identifies the live backing buffer, without transferring ownership.</summary>
    public int Buffer { readonly get => buffer; set { buffer = value; assigned |= 1; } }
    /// <summary>Specifies the texel interpretation.</summary>
    public TextureFormat Format { readonly get => format; set { format = value; assigned |= 2; } }
    /// <summary>Selects whole-store or explicit-range semantics.</summary>
    public GpuBufferTextureMode Mode { readonly get => mode; set { mode = value; assigned |= 4; } }
    /// <summary>Specifies the native-width first byte of a range.</summary>
    public nint Offset { readonly get => offset; set { offset = value; assigned |= 8; } }
    /// <summary>Specifies the native-width range length.</summary>
    public nint Size { readonly get => size; set { size = value; assigned |= 16; } }
    #endregion

    #region Internal API
    /// <summary>Rejects incomplete requests and properties incompatible with the selected mode.</summary>
    internal readonly void Validate()
    {
        if ((assigned & 7) != 7 || buffer == 0 || !Enum.IsDefined(mode))
            throw new ArgumentException("Buffer texture initialization requires backing, format and mode.");
        if (mode == GpuBufferTextureMode.Whole ? (assigned & 24) != 0 : (assigned & 24) != 24 || offset < 0 || size <= 0)
            throw new ArgumentException("Whole views reject ranges; range views require nonnegative offset and positive size.");
        if (format is not (TextureFormat.R8 or TextureFormat.R16 or TextureFormat.R16f or TextureFormat.R32f
            or TextureFormat.R8i or TextureFormat.R16i or TextureFormat.R32i
            or TextureFormat.R8ui or TextureFormat.R16ui or TextureFormat.R32ui
            or TextureFormat.Rg8 or TextureFormat.Rg16 or TextureFormat.Rg16f or TextureFormat.Rg32f
            or TextureFormat.Rg8i or TextureFormat.Rg16i or TextureFormat.Rg32i
            or TextureFormat.Rg8ui or TextureFormat.Rg16ui or TextureFormat.Rg32ui
            or TextureFormat.Rgb32f or TextureFormat.Rgb32i or TextureFormat.Rgb32ui
            or TextureFormat.Rgba8 or TextureFormat.Rgba16 or TextureFormat.Rgba16f or TextureFormat.Rgba32f
            or TextureFormat.Rgba8i or TextureFormat.Rgba16i or TextureFormat.Rgba32i
            or TextureFormat.Rgba8ui or TextureFormat.Rgba16ui or TextureFormat.Rgba32ui))
            throw new ArgumentException("The selected format is not valid for buffer textures.");
    }
    #endregion
}
