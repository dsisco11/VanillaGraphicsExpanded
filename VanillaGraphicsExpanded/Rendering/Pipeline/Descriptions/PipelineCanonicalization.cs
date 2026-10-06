using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Resolves inactive configuration to explicit native defaults without carrying ambient state.</summary>
internal static class PipelineCanonicalization
{
    #region Public API
    #region State normalization
    /// <summary>Resets disabled comparisons and stencil faces; enabled descriptions retain their entire authored configuration.</summary>
    public static DepthStencilDesc DepthStencil(DepthStencilDesc value) => value with
    {
        DepthComparison = value.DepthTest ? value.DepthComparison : DepthFunction.Less,
        Front = value.StencilTest ? Stencil(value.Front) : new(), Back = value.StencilTest ? Stencil(value.Back) : new()
    };

    /// <summary>Neutralizes inactive culling and bias while retaining independent size and raster controls.</summary>
    public static RasterizerDesc Rasterizer(RasterizerDesc value) => value with
    {
        CullMode = value.Cull ? value.CullMode : CullFaceMode.Back,
        DepthBiasFactor = value.OffsetFill || value.OffsetLine || value.OffsetPoint ? value.DepthBiasFactor : 0,
        DepthBiasUnits = value.OffsetFill || value.OffsetLine || value.OffsetPoint ? value.DepthBiasUnits : 0,
        AlphaComparison = value.AlphaTest ? value.AlphaComparison : AlphaFunction.Always,
        AlphaReference = value.AlphaTest ? value.AlphaReference : 0,
        LineStippleFactor = value.LineStipple ? value.LineStippleFactor : 1,
        LineStipplePattern = value.LineStipple ? value.LineStipplePattern : ushort.MaxValue,
        PolygonStipplePattern = value.PolygonStipple ? value.PolygonStipplePattern : RasterizerDesc.FullPolygonStipple
    };

    /// <summary>Retains color masks even when blending is disabled, and resolves ignored factors for min/max equations.</summary>
    public static ColorBlendDesc Blend(ColorBlendDesc value)
    {
        if (!value.Enabled) return new() { WriteRed = value.WriteRed, WriteGreen = value.WriteGreen,
            WriteBlue = value.WriteBlue, WriteAlpha = value.WriteAlpha };
        return value with
        {
            SourceRgb = UsesFactors(value.RgbEquation) ? value.SourceRgb : BlendingFactorSrc.One,
            DestinationRgb = UsesFactors(value.RgbEquation) ? value.DestinationRgb : BlendingFactorDest.Zero,
            SourceAlpha = UsesFactors(value.AlphaEquation) ? value.SourceAlpha : BlendingFactorSrc.One,
            DestinationAlpha = UsesFactors(value.AlphaEquation) ? value.DestinationAlpha : BlendingFactorDest.Zero
        };
    }

    /// <summary>Represents disabled masks by an explicit all-ones policy rather than device-dependent key data.</summary>
    public static SamplingDesc Sampling(SamplingDesc value) => value with
    {
        Coverage = value.CoverageEnabled ? value.Coverage : 1,
        CoverageInvert = value.CoverageEnabled && value.CoverageInvert,
        Masks = value.MaskEnabled ? Masks(value.Masks) : new PipelineValues<uint>([]),
        MinimumSampleShading = value.SampleShading ? value.MinimumSampleShading : 0
    };

    /// <summary>Removes ignored restart index and patch count from nonparticipating primitive configurations.</summary>
    public static PrimitiveAssemblyDesc Assembly(PrimitiveAssemblyDesc value) => value with
    {
        RestartIndex = value.Restart && !value.FixedIndexRestart ? value.RestartIndex : 0,
        PatchVertices = value.Topology == PrimitiveType.Patches ? value.PatchVertices : 3
    };
    #endregion

    #region Equation semantics
    /// <summary>Identifies equations whose results depend on authored factors.</summary>
    public static bool UsesFactors(BlendEquationMode equation) => equation is not (BlendEquationMode.Min or BlendEquationMode.Max);
    #endregion
    #endregion

    #region Private
    /// <summary>Removes only an all-ones suffix, whose values are supplied explicitly by GetMaskWord.</summary>
    private static PipelineValues<uint> Masks(PipelineValues<uint>? values)
    {
        int count = values?.Count ?? 0;
        while (count > 0 && values![count - 1] == uint.MaxValue) count--;
        return new(values is null ? [] : values.Take(count));
    }

    /// <summary>Resolves all-ones masks to the eight-bit stencil formats supported by target signatures.</summary>
    private static StencilFaceDesc Stencil(StencilFaceDesc value) => value with
    {
        ReadMask = value.ReadMask & 255, WriteMask = value.WriteMask & 255
    };
    #endregion
}
