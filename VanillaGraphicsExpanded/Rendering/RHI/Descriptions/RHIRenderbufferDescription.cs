using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares renderbuffer extent, representation and sampling intent.</summary>
public struct RHIRenderbufferDescription
{
    private ulong assigned;
    private RHIFormat format;
    private int width;
    private int height;
    private int samples;
    private int coverageSamples;
    private string? label;

    #region Public API
    /// <summary>Supplies Format while retaining explicit assignment intent.</summary>
    public RHIFormat Format { readonly get => format; set { format = value; assigned |= 1UL << 0; } }
    /// <summary>Supplies Width while retaining explicit assignment intent.</summary>
    public int Width { readonly get => width; set { width = value; assigned |= 1UL << 1; } }
    /// <summary>Supplies Height while retaining explicit assignment intent.</summary>
    public int Height { readonly get => height; set { height = value; assigned |= 1UL << 2; } }
    /// <summary>Supplies Samples while retaining explicit assignment intent.</summary>
    public int Samples { readonly get => samples; set { samples = value; assigned |= 1UL << 3; } }
    /// <summary>Supplies CoverageSamples while retaining explicit assignment intent.</summary>
    public int CoverageSamples { readonly get => coverageSamples; set { coverageSamples = value; assigned |= 1UL << 4; } }
    /// <summary>Supplies Label while retaining explicit assignment intent.</summary>
    public string? Label { readonly get => label; set { label = value; assigned |= 1UL << 5; } }
    #endregion

    #region Internal API
    /// <summary>Reports supplied fields without exposing a writable assignment mask.</summary>
    internal readonly bool Has(int field) => (assigned & (1UL << field)) != 0;

    #endregion
}
