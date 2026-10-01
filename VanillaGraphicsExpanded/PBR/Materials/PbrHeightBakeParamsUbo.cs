using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// CPU-side UBO for PBR height-bake shader parameters.
/// Layout matches VgePbrHeightBakeParamsUBO in GLSL (528 bytes).
/// Shared across all height-bake passes (luminance, gaussian, gradient, Poisson solver, etc.).
/// </summary>
internal sealed class PbrHeightBakeParamsUbo : CpuUniformBuffer
{
    public const string BlockName = "VgePbrHeightBakeParamsUBO";
    
    // Offsets for various parameter groups
    private const int OffsetMultigridFineSize = 0;       // ivec4 at 0
    private const int OffsetMultigridCoarseSize = 16;    // ivec4 at 16
    private const int OffsetCommonSize = 32;             // ivec4 at 32 (shared by most passes)
    private const int OffsetGaussDir = 48;               // ivec4 at 48
    private const int OffsetGaussParams = 64;            // ivec4 at 64 (radius, relContrast/mode)
    private const int OffsetSubParams = 80;              // vec4 at 80 (eps, vMax for sub/combine)
    private const int OffsetCombineWeights = 96;         // vec4 at 96 (w1,w2,w3,0)
    private const int OffsetGradientParams = 112;        // vec4 at 112 (gain, maxSlope, edgeT0, edgeT1)
    private const int OffsetLuminanceAtlasRect = 128;    // ivec4 at 128 (x,y,w,h)
    private const int OffsetLuminanceDstSize = 144;      // ivec4 at 144
    private const int OffsetSolverSize = 160;            // ivec4 at 160
    private const int OffsetTileSize = 176;              // ivec4 at 176
    private const int OffsetViewportOrigin = 192;        // ivec4 at 192
    private const int OffsetNormalizeParams = 208;       // vec4 at 208 (mean, invNeg, invPos, heightStrength)
    private const int OffsetNormalizeGamma = 224;        // vec4 at 224 (gamma, 0, 0, 0)
    private const int OffsetPackParams = 240;            // vec4 at 240 (normalStrength, normalScale, depthScale, eps)
    
    // Kernel weights array (up to 64 weights)
    private const int OffsetKernelWeights = 256;         // vec4[16] starting at 256 (256 bytes total)
    // Total size: 512 bytes + 16 for alignment = 528 bytes

    public PbrHeightBakeParamsUbo() : base(528)
    {
    }

    #region Common Size (used by most passes)
    
    /// <summary>
    /// Common texture size for most passes (width, height).
    /// Offset 32.
    /// </summary>
    public (int width, int height) CommonSize
    {
        set
        {
            WriteIntVector4(OffsetCommonSize, value.width, value.height, 0, 0);
        }
    }

    #endregion

    #region Gaussian Pass Parameters

    /// <summary>
    /// Gaussian blur direction (1,0,0,0) for horizontal or (0,1,0,0) for vertical.
    /// Offset 48.
    /// </summary>
    public (int x, int y) GaussianDirection
    {
        set
        {
            WriteIntVector4(OffsetGaussDir, value.x, value.y, 0, 0);
        }
    }

    /// <summary>
    /// Gaussian parameters: radius and mode flags.
    /// Offset 64.
    /// </summary>
    public (int radius, int relContrast) GaussianParams
    {
        set
        {
            WriteIntVector4(OffsetGaussParams, value.radius, value.relContrast, 0, 0);
        }
    }

    /// <summary>
    /// Kernel weights for gaussian blur (up to 64 weights).
    /// Each vec4 holds 4 weights. Writes starting at offset 256.
    /// </summary>
    public void SetKernelWeights(float[] weights, int count)
    {
        // Validate the complete source and fixed destination capacity before writing the first slot.
        System.ArgumentNullException.ThrowIfNull(weights);
        if ((uint)count > 64) throw new System.ArgumentOutOfRangeException(nameof(count));
        if (weights.Length < count) throw new System.ArgumentException("Source must contain the requested weights.", nameof(weights));
        int vec4Count = (count + 3) / 4;
        for (int i = 0; i < vec4Count; i++)
        {
            int baseIdx = i * 4;
            float w0 = baseIdx + 0 < count ? weights[baseIdx + 0] : 0f;
            float w1 = baseIdx + 1 < count ? weights[baseIdx + 1] : 0f;
            float w2 = baseIdx + 2 < count ? weights[baseIdx + 2] : 0f;
            float w3 = baseIdx + 3 < count ? weights[baseIdx + 3] : 0f;
            WriteVector4(OffsetKernelWeights + (i * 16), new(w0, w1, w2, w3));
        }
    }

    #endregion

    #region Sub Pass Parameters

    /// <summary>
    /// Subtraction/combine epsilon and value max parameters.
    /// Offset 80.
    /// </summary>
    public (float eps, float vMax) SubParams
    {
        set
        {
            WriteVector4(OffsetSubParams, new(value.eps, value.vMax, 0f, 0f));
        }
    }

    #endregion

    #region Combine Pass Parameters

    /// <summary>
    /// Band-pass combine weights (w1, w2, w3).
    /// Offset 96.
    /// </summary>
    public (float w1, float w2, float w3) CombineWeights
    {
        set
        {
            WriteVector4(OffsetCombineWeights, new(value.w1, value.w2, value.w3, 0f));
        }
    }

    #endregion

    #region Gradient Pass Parameters

    /// <summary>
    /// Gradient computation parameters.
    /// Offset 112.
    /// </summary>
    public (float gain, float maxSlope, float edgeT0, float edgeT1) GradientParams
    {
        set
        {
            WriteVector4(OffsetGradientParams, new(value.gain, value.maxSlope, value.edgeT0, value.edgeT1));
        }
    }

    #endregion

    #region Luminance Pass Parameters

    /// <summary>
    /// Atlas rect for luminance extraction (x, y, width, height).
    /// Offset 128.
    /// </summary>
    public (int x, int y, int w, int h) LuminanceAtlasRect
    {
        set
        {
            WriteIntVector4(OffsetLuminanceAtlasRect, value.x, value.y, value.w, value.h);
        }
    }

    /// <summary>
    /// Destination size for luminance pass.
    /// Offset 144.
    /// </summary>
    public (int width, int height) LuminanceDstSize
    {
        set
        {
            WriteIntVector4(OffsetLuminanceDstSize, value.width, value.height, 0, 0);
        }
    }

    #endregion

    #region Pack to Atlas Parameters

    /// <summary>
    /// Solver size for pack-to-atlas pass.
    /// Offset 160.
    /// </summary>
    public (int width, int height) SolverSize
    {
        set
        {
            WriteIntVector4(OffsetSolverSize, value.width, value.height, 0, 0);
        }
    }

    /// <summary>
    /// Tile size for pack-to-atlas pass.
    /// Offset 176.
    /// </summary>
    public (int width, int height) TileSize
    {
        set
        {
            WriteIntVector4(OffsetTileSize, value.width, value.height, 0, 0);
        }
    }

    /// <summary>
    /// Viewport origin for pack-to-atlas pass.
    /// Offset 192.
    /// </summary>
    public (int x, int y) ViewportOrigin
    {
        set
        {
            WriteIntVector4(OffsetViewportOrigin, value.x, value.y, 0, 0);
        }
    }

    #endregion

    #region Normalize Pass Parameters

    /// <summary>
    /// Normalization parameters (mean, invNeg, invPos, heightStrength).
    /// Offset 208.
    /// </summary>
    public (float mean, float invNeg, float invPos, float heightStrength) NormalizeParams
    {
        set
        {
            WriteVector4(OffsetNormalizeParams, new(value.mean, value.invNeg, value.invPos, value.heightStrength));
        }
    }

    /// <summary>
    /// Gamma value for normalize pass.
    /// Offset 224.
    /// </summary>
    public float NormalizeGamma
    {
        set
        {
            WriteVector4(OffsetNormalizeGamma, new(value, 0f, 0f, 0f));
        }
    }

    #endregion

    #region Pack Parameters

    /// <summary>
    /// Normal/depth packing parameters.
    /// Offset 240.
    /// </summary>
    public (float normalStrength, float normalScale, float depthScale, float eps) PackParams
    {
        set
        {
            WriteVector4(OffsetPackParams, new(value.normalStrength, value.normalScale, value.depthScale, value.eps));
        }
    }

    #endregion

    #region Multigrid Solver Parameters

    /// <summary>
    /// Fine level size for multigrid solver.
    /// Offset 0.
    /// </summary>
    public (int width, int height) MultigridFineSize
    {
        set
        {
            WriteIntVector4(OffsetMultigridFineSize, value.width, value.height, 0, 0);
        }
    }

    /// <summary>
    /// Coarse level size for multigrid solver.
    /// Offset 16.
    /// </summary>
    public (int width, int height) MultigridCoarseSize
    {
        set
        {
            WriteIntVector4(OffsetMultigridCoarseSize, value.width, value.height, 0, 0);
        }
    }

    #endregion
}
