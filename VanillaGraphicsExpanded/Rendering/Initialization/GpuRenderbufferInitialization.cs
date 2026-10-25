using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains required renderbuffer storage inputs separately from their published result.</summary>
internal struct GpuRenderbufferInitialization
{
    private byte assigned;
    private RenderbufferStorage format;
    private int width, height, samples;

    #region Public API
    /// <summary>Specifies the required storage format.</summary>
    public RenderbufferStorage Format { readonly get => format; set { format = value; assigned |= 1; } }
    /// <summary>Specifies the required width.</summary>
    public int Width { readonly get => width; set { width = value; assigned |= 2; } }
    /// <summary>Specifies the required height.</summary>
    public int Height { readonly get => height; set { height = value; assigned |= 4; } }
    /// <summary>Specifies samples; omission requests non-multisample storage.</summary>
    public int Samples { readonly get => samples; set { samples = value; assigned |= 8; } }
    #endregion

    #region Internal API
    /// <summary>Rejects incomplete and invalid storage requests before native allocation.</summary>
    internal readonly void Validate()
    {
        if ((assigned & 7) != 7 || !Enum.IsDefined(format) || width <= 0 || height <= 0 || samples < 0)
            throw new ArgumentException("Renderbuffer initialization requires a format, positive extents and nonnegative samples.");
        int limit = GpuSupport.Graphics.MaxRenderbufferSize;
        if (limit > 0 && (width > limit || height > limit))
            throw new ArgumentOutOfRangeException(nameof(Width), "Renderbuffer extents exceed the cached implementation limit.");
    }
    #endregion
}
