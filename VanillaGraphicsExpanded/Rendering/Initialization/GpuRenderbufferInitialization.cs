using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains required renderbuffer storage inputs separately from their published result.</summary>
internal struct GpuRenderbufferInitialization
{
    private byte assigned;
    private RenderbufferStorage format;
    private int width, height, samples, coverageSamples;

    #region Public API
    /// <summary>Specifies the required storage format.</summary>
    public RenderbufferStorage Format { readonly get => format; set { format = value; assigned |= 1; } }
    /// <summary>Specifies the required width.</summary>
    public int Width { readonly get => width; set { width = value; assigned |= 2; } }
    /// <summary>Specifies the required height.</summary>
    public int Height { readonly get => height; set { height = value; assigned |= 4; } }
    /// <summary>Specifies samples; omission requests non-multisample storage.</summary>
    public int Samples { readonly get => samples; set { samples = value; assigned |= 8; } }
    /// <summary>Supplies an optional coverage-sample request distinct from color sample count.</summary>
    public int CoverageSamples { readonly get => coverageSamples; set { coverageSamples = value; assigned |= 16; } }
    #endregion

    #region Internal API
    /// <summary>Reports optional coverage intent without treating omitted zero as a request.</summary>
    internal readonly bool HasCoverageSamples => (assigned & 16) != 0;
    /// <summary>Rejects incomplete and invalid storage requests before native allocation.</summary>
    internal readonly void Validate()
    {
        if ((assigned & 7) != 7 || !Enum.IsDefined(format) || width <= 0 || height <= 0 || samples < 0)
            throw new ArgumentException("Renderbuffer initialization requires a format, positive extents and nonnegative samples.");
        if (HasCoverageSamples && (samples <= 0 || coverageSamples < samples))
            throw new ArgumentException("Coverage storage requires positive color samples and at least that many coverage samples.");
        var capabilities = RHI.RHICommandContext.Current.Capabilities;
        if (HasCoverageSamples && !capabilities.CoverageSampling) throw new NotSupportedException("Coverage sampling is unavailable.");
        int limit = capabilities.MaxRenderbufferSize;
        if (limit > 0 && (width > limit || height > limit))
            throw new ArgumentOutOfRangeException(nameof(Width), "Renderbuffer extents exceed the cached implementation limit.");
    }
    #endregion
}
