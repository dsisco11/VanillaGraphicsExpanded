using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Selects mutable image definitions or immutable allocation without changing wrapper policy.</summary>
internal enum GpuTextureAllocationMode { Mutable, Immutable }

/// <summary>Records legal texture allocation inputs, distinguishing omitted properties from explicit values.</summary>
internal partial struct GpuTextureInitialization
{
    private Inputs assigned;
    private TextureTarget target;
    private GpuTextureAllocationMode allocationMode;
    private PixelInternalFormat internalFormat;
    private int width;
    private int height;
    private int depth;
    private int levels;
    private int samples;
    private bool fixedSampleLocations;
    private int baseLevel;
    private int maxLevel;

    /// <summary>Names input properties for manager-side omission validation; callers cannot change the mask.</summary>
    [Flags]
    internal enum Inputs { Target = 1, AllocationMode = 2, InternalFormat = 4, Width = 8, Height = 16, Depth = 32, Levels = 64, Samples = 128, FixedSampleLocations = 256, BaseLevel = 512, MaxLevel = 1024 }

    #region Public API
    /// <summary>Supplies the requested target; target-specific validation occurs before dispatch.</summary>
    public TextureTarget Target { readonly get => target; set { target = value; assigned |= Inputs.Target; } }
    /// <summary>Supplies the requested allocation mode; target-specific validation occurs before dispatch.</summary>
    public GpuTextureAllocationMode AllocationMode { readonly get => allocationMode; set { allocationMode = value; assigned |= Inputs.AllocationMode; } }
    /// <summary>Supplies the requested internal format; target-specific validation occurs before dispatch.</summary>
    public PixelInternalFormat InternalFormat { readonly get => internalFormat; set { internalFormat = value; assigned |= Inputs.InternalFormat; } }
    /// <summary>Supplies the requested width; target-specific validation occurs before dispatch.</summary>
    public int Width { readonly get => width; set { width = value; assigned |= Inputs.Width; } }
    /// <summary>Supplies the requested height; target-specific validation occurs before dispatch.</summary>
    public int Height { readonly get => height; set { height = value; assigned |= Inputs.Height; } }
    /// <summary>Supplies the requested depth; target-specific validation occurs before dispatch.</summary>
    public int Depth { readonly get => depth; set { depth = value; assigned |= Inputs.Depth; } }
    /// <summary>Supplies the requested levels; target-specific validation occurs before dispatch.</summary>
    public int Levels { readonly get => levels; set { levels = value; assigned |= Inputs.Levels; } }
    /// <summary>Supplies the requested samples; target-specific validation occurs before dispatch.</summary>
    public int Samples { readonly get => samples; set { samples = value; assigned |= Inputs.Samples; } }
    /// <summary>Supplies the requested fixed sample locations; target-specific validation occurs before dispatch.</summary>
    public bool FixedSampleLocations { readonly get => fixedSampleLocations; set { fixedSampleLocations = value; assigned |= Inputs.FixedSampleLocations; } }
    /// <summary>Supplies the requested base level; target-specific validation occurs before dispatch.</summary>
    public int BaseLevel { readonly get => baseLevel; set { baseLevel = value; assigned |= Inputs.BaseLevel; } }
    /// <summary>Supplies the requested max level; target-specific validation occurs before dispatch.</summary>
    public int MaxLevel { readonly get => maxLevel; set { maxLevel = value; assigned |= Inputs.MaxLevel; } }
    #endregion

    #region Internal API
    /// <summary>Reports assignment intent without exposing writable flags or manufacturing native defaults.</summary>
    internal readonly bool IsAssigned(Inputs input) => (assigned & input) == input;
    #endregion
}
