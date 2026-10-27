using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects a view-relative representation of an original immutable allocation.</summary>
public struct RHITextureViewDescription
{
    private ulong assigned;
    private GpuTextureRef source;
    private TextureDimension dimension;
    private TextureFormat format;
    private int baseMip;
    private int mipCount;
    private int baseLayer;
    private int layerCount;
    private string? label;

    #region Public API
    /// <summary>Supplies Source while retaining explicit assignment intent.</summary>
    public GpuTextureRef Source { readonly get => source; set { source = value; assigned |= 1UL << 0; } }
    /// <summary>Supplies Dimension while retaining explicit assignment intent.</summary>
    public TextureDimension Dimension { readonly get => dimension; set { dimension = value; assigned |= 1UL << 1; } }
    /// <summary>Supplies Format while retaining explicit assignment intent.</summary>
    public TextureFormat Format { readonly get => format; set { format = value; assigned |= 1UL << 2; } }
    /// <summary>Supplies BaseMip while retaining explicit assignment intent.</summary>
    public int BaseMip { readonly get => baseMip; set { baseMip = value; assigned |= 1UL << 3; } }
    /// <summary>Supplies MipCount while retaining explicit assignment intent.</summary>
    public int MipCount { readonly get => mipCount; set { mipCount = value; assigned |= 1UL << 4; } }
    /// <summary>Supplies BaseLayer while retaining explicit assignment intent.</summary>
    public int BaseLayer { readonly get => baseLayer; set { baseLayer = value; assigned |= 1UL << 5; } }
    /// <summary>Supplies LayerCount while retaining explicit assignment intent.</summary>
    public int LayerCount { readonly get => layerCount; set { layerCount = value; assigned |= 1UL << 6; } }
    /// <summary>Supplies Label while retaining explicit assignment intent.</summary>
    public string? Label { readonly get => label; set { label = value; assigned |= 1UL << 7; } }
    #endregion

    #region Internal API
    /// <summary>Reports supplied fields without exposing a writable assignment mask.</summary>
    internal readonly bool Has(int field) => (assigned & (1UL << field)) != 0;

    #endregion
}
