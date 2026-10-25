using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Distinguishes a whole backing store from an explicitly selected byte range.</summary>
internal enum GpuBufferTextureMode { Whole, Range }

/// <summary>Records explicit legal buffer-texture creation inputs with automatic assignment tracking.</summary>
internal struct GpuBufferTextureInitialization
{
    private byte assigned;
    private int buffer;
    private SizedInternalFormat format;
    private GpuBufferTextureMode mode;
    private nint offset, size;

    #region Public API
    /// <summary>Identifies the live backing buffer, without transferring ownership.</summary>
    public int Buffer { readonly get => buffer; set { buffer = value; assigned |= 1; } }
    /// <summary>Specifies the texel interpretation.</summary>
    public SizedInternalFormat Format { readonly get => format; set { format = value; assigned |= 2; } }
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
        if (mode == GpuBufferTextureMode.Range && !GpuSupport.Graphics.SupportsTextureBufferRange)
            throw new NotSupportedException("Buffer texture ranges are unavailable.");
        if (format is not (SizedInternalFormat.R8 or SizedInternalFormat.R16 or SizedInternalFormat.R16f or SizedInternalFormat.R32f
            or SizedInternalFormat.R8i or SizedInternalFormat.R16i or SizedInternalFormat.R32i
            or SizedInternalFormat.R8ui or SizedInternalFormat.R16ui or SizedInternalFormat.R32ui
            or SizedInternalFormat.Rg8 or SizedInternalFormat.Rg16 or SizedInternalFormat.Rg16f or SizedInternalFormat.Rg32f
            or SizedInternalFormat.Rg8i or SizedInternalFormat.Rg16i or SizedInternalFormat.Rg32i
            or SizedInternalFormat.Rg8ui or SizedInternalFormat.Rg16ui or SizedInternalFormat.Rg32ui
            or SizedInternalFormat.Rgb32f or SizedInternalFormat.Rgb32i or SizedInternalFormat.Rgb32ui
            or SizedInternalFormat.Rgba8 or SizedInternalFormat.Rgba16 or SizedInternalFormat.Rgba16f or SizedInternalFormat.Rgba32f
            or SizedInternalFormat.Rgba8i or SizedInternalFormat.Rgba16i or SizedInternalFormat.Rgba32i
            or SizedInternalFormat.Rgba8ui or SizedInternalFormat.Rgba16ui or SizedInternalFormat.Rgba32ui))
            throw new ArgumentException("The selected format is not valid for buffer textures.");
    }
    #endregion
}
