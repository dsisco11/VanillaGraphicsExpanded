using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares sampling policy without mirroring native sampler state in metadata.</summary>
public struct RHISamplerDescription
{
    private ulong assigned;
    private SamplerFilter minFilter;
    private SamplerFilter magFilter;
    private SamplerAddressMode addressS;
    private SamplerAddressMode addressT;
    private SamplerAddressMode addressR;
    private float minLod;
    private float maxLod;
    private float lodBias;
    private bool compare;
    private ComparisonFunction comparison;
    private System.Numerics.Vector4 borderColor;
    private float anisotropy;
    private string? label;

    #region Public API
    /// <summary>Supplies MinFilter while retaining explicit assignment intent.</summary>
    public SamplerFilter MinFilter { readonly get => minFilter; set { minFilter = value; assigned |= 1UL << 0; } }
    /// <summary>Supplies MagFilter while retaining explicit assignment intent.</summary>
    public SamplerFilter MagFilter { readonly get => magFilter; set { magFilter = value; assigned |= 1UL << 1; } }
    /// <summary>Supplies AddressS while retaining explicit assignment intent.</summary>
    public SamplerAddressMode AddressS { readonly get => addressS; set { addressS = value; assigned |= 1UL << 2; } }
    /// <summary>Supplies AddressT while retaining explicit assignment intent.</summary>
    public SamplerAddressMode AddressT { readonly get => addressT; set { addressT = value; assigned |= 1UL << 3; } }
    /// <summary>Supplies AddressR while retaining explicit assignment intent.</summary>
    public SamplerAddressMode AddressR { readonly get => addressR; set { addressR = value; assigned |= 1UL << 4; } }
    /// <summary>Supplies MinLod while retaining explicit assignment intent.</summary>
    public float MinLod { readonly get => minLod; set { minLod = value; assigned |= 1UL << 5; } }
    /// <summary>Supplies MaxLod while retaining explicit assignment intent.</summary>
    public float MaxLod { readonly get => maxLod; set { maxLod = value; assigned |= 1UL << 6; } }
    /// <summary>Supplies LodBias while retaining explicit assignment intent.</summary>
    public float LodBias { readonly get => lodBias; set { lodBias = value; assigned |= 1UL << 7; } }
    /// <summary>Supplies Compare while retaining explicit assignment intent.</summary>
    public bool Compare { readonly get => compare; set { compare = value; assigned |= 1UL << 8; } }
    /// <summary>Supplies Comparison while retaining explicit assignment intent.</summary>
    public ComparisonFunction Comparison { readonly get => comparison; set { comparison = value; assigned |= 1UL << 9; } }
    /// <summary>Supplies BorderColor while retaining explicit assignment intent.</summary>
    public System.Numerics.Vector4 BorderColor { readonly get => borderColor; set { borderColor = value; assigned |= 1UL << 10; } }
    /// <summary>Supplies Anisotropy while retaining explicit assignment intent.</summary>
    public float Anisotropy { readonly get => anisotropy; set { anisotropy = value; assigned |= 1UL << 11; } }
    /// <summary>Supplies Label while retaining explicit assignment intent.</summary>
    public string? Label { readonly get => label; set { label = value; assigned |= 1UL << 12; } }
    #endregion

    #region Internal API
    /// <summary>Reports supplied fields without exposing a writable assignment mask.</summary>
    internal readonly bool Has(int field) => (assigned & (1UL << field)) != 0;

    #endregion
}
