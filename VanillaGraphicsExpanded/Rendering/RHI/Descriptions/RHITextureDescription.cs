using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares image allocation intent separately from contents and published metadata.</summary>
public struct RHITextureDescription
{
    private ulong assigned;
    private RHITextureDimension dimension;
    private RHIFormat format;
    private int width;
    private int height;
    private int depth;
    private int mipLevels;
    private RHIStorageMode storage;
    private int samples;
    private bool fixedSampleLocations;
    private int baseLevel;
    private int maxLevel;
    private TextureFilterMode filter;
    private string? label;

    #region Public API
    /// <summary>Supplies Dimension while retaining explicit assignment intent.</summary>
    public RHITextureDimension Dimension { readonly get => dimension; set { dimension = value; assigned |= 1UL << 0; } }
    /// <summary>Supplies Format while retaining explicit assignment intent.</summary>
    public RHIFormat Format { readonly get => format; set { format = value; assigned |= 1UL << 1; } }
    /// <summary>Supplies Width while retaining explicit assignment intent.</summary>
    public int Width { readonly get => width; set { width = value; assigned |= 1UL << 2; } }
    /// <summary>Supplies Height while retaining explicit assignment intent.</summary>
    public int Height { readonly get => height; set { height = value; assigned |= 1UL << 3; } }
    /// <summary>Supplies Depth while retaining explicit assignment intent.</summary>
    public int Depth { readonly get => depth; set { depth = value; assigned |= 1UL << 4; } }
    /// <summary>Supplies MipLevels while retaining explicit assignment intent.</summary>
    public int MipLevels { readonly get => mipLevels; set { mipLevels = value; assigned |= 1UL << 5; } }
    /// <summary>Supplies Storage while retaining explicit assignment intent.</summary>
    public RHIStorageMode Storage { readonly get => storage; set { storage = value; assigned |= 1UL << 6; } }
    /// <summary>Supplies Samples while retaining explicit assignment intent.</summary>
    public int Samples { readonly get => samples; set { samples = value; assigned |= 1UL << 7; } }
    /// <summary>Supplies FixedSampleLocations while retaining explicit assignment intent.</summary>
    public bool FixedSampleLocations { readonly get => fixedSampleLocations; set { fixedSampleLocations = value; assigned |= 1UL << 8; } }
    /// <summary>Supplies BaseLevel while retaining explicit assignment intent.</summary>
    public int BaseLevel { readonly get => baseLevel; set { baseLevel = value; assigned |= 1UL << 9; } }
    /// <summary>Supplies MaxLevel while retaining explicit assignment intent.</summary>
    public int MaxLevel { readonly get => maxLevel; set { maxLevel = value; assigned |= 1UL << 10; } }
    /// <summary>Supplies Filter while retaining explicit assignment intent.</summary>
    public TextureFilterMode Filter { readonly get => filter; set { filter = value; assigned |= 1UL << 11; } }
    /// <summary>Supplies Label while retaining explicit assignment intent.</summary>
    public string? Label { readonly get => label; set { label = value; assigned |= 1UL << 12; } }
    #endregion

    #region Internal API
    /// <summary>Reports supplied fields without exposing a writable assignment mask.</summary>
    internal readonly bool Has(int field) => (assigned & (1UL << field)) != 0;

    #endregion
}
