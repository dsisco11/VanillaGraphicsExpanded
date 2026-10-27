using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects an original buffer association and explicit whole or ranged texel interpretation.</summary>
public struct RHIBufferTextureDescription
{
    private ulong assigned;
    private GpuBufferRef buffer;
    private TextureFormat format;
    private bool wholeBuffer;
    private nint offset;
    private nint length;
    private string? label;

    #region Public API
    /// <summary>Supplies Buffer while retaining explicit assignment intent.</summary>
    public GpuBufferRef Buffer { readonly get => buffer; set { buffer = value; assigned |= 1UL << 0; } }
    /// <summary>Supplies Format while retaining explicit assignment intent.</summary>
    public TextureFormat Format { readonly get => format; set { format = value; assigned |= 1UL << 1; } }
    /// <summary>Supplies WholeBuffer while retaining explicit assignment intent.</summary>
    public bool WholeBuffer { readonly get => wholeBuffer; set { wholeBuffer = value; assigned |= 1UL << 2; } }
    /// <summary>Supplies Offset while retaining explicit assignment intent.</summary>
    public nint Offset { readonly get => offset; set { offset = value; assigned |= 1UL << 3; } }
    /// <summary>Supplies Length while retaining explicit assignment intent.</summary>
    public nint Length { readonly get => length; set { length = value; assigned |= 1UL << 4; } }
    /// <summary>Supplies Label while retaining explicit assignment intent.</summary>
    public string? Label { readonly get => label; set { label = value; assigned |= 1UL << 5; } }
    #endregion

    #region Internal API
    /// <summary>Reports supplied fields without exposing a writable assignment mask.</summary>
    internal readonly bool Has(int field) => (assigned & (1UL << field)) != 0;

    #endregion
}
