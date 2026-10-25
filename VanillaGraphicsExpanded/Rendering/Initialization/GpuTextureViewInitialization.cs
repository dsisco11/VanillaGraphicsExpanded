using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Records only explicit immutable-view creation inputs.</summary>
internal struct GpuTextureViewInitialization
{
    private byte assigned;
    private int valueSource;
    private TextureTarget valueTarget;
    private PixelInternalFormat valueFormat;
    private int valueBaseLevel;
    private int valueLevelCount;
    private int valueBaseLayer;
    private int valueLayerCount;
    #region Public API
    /// <summary>Supplies the required Source input.</summary>
    public int Source { readonly get => valueSource; set { valueSource = value; assigned |= 1; } }
    /// <summary>Supplies the required Target input.</summary>
    public TextureTarget Target { readonly get => valueTarget; set { valueTarget = value; assigned |= 2; } }
    /// <summary>Supplies the required Format input.</summary>
    public PixelInternalFormat Format { readonly get => valueFormat; set { valueFormat = value; assigned |= 4; } }
    /// <summary>Supplies the required BaseLevel input.</summary>
    public int BaseLevel { readonly get => valueBaseLevel; set { valueBaseLevel = value; assigned |= 8; } }
    /// <summary>Supplies the required LevelCount input.</summary>
    public int LevelCount { readonly get => valueLevelCount; set { valueLevelCount = value; assigned |= 16; } }
    /// <summary>Supplies the required BaseLayer input.</summary>
    public int BaseLayer { readonly get => valueBaseLayer; set { valueBaseLayer = value; assigned |= 32; } }
    /// <summary>Supplies the required LayerCount input.</summary>
    public int LayerCount { readonly get => valueLayerCount; set { valueLayerCount = value; assigned |= 64; } }
    #endregion

    #region Internal API
    /// <summary>Rejects omitted selections before any native object is created.</summary>
    internal readonly void Validate()
    {
        if (assigned != 127 || Source == 0 || BaseLevel < 0 || BaseLayer < 0 || LevelCount <= 0 || LayerCount <= 0)
            throw new ArgumentException("Texture views require a source, target, format and explicit positive selection.");
        if (!GpuSupport.Graphics.SupportsTextureView) throw new NotSupportedException("Texture views are unavailable.");
    }
    #endregion
}
