using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Immutable potential native write groups derived for one prepared graphics declaration.</summary>
/// <remarks>These masks describe relevance, not cache validity or applied values. Native call groups
/// reuse the shared owner's field vocabulary. Application and boundary authority remain independent.</remarks>
internal sealed class GraphicsStateRelevance
{
    internal DepthStateKnowledge Depth { get; }
    internal StencilStateKnowledge Stencil { get; }
    internal RasterizerStateKnowledge Rasterizer { get; }
    internal SamplingStateKnowledge Sampling { get; }
    internal PrimitiveAssemblyStateKnowledge Assembly { get; }
    internal OutputStateKnowledge Output { get; } = OutputStateKnowledge.All;
    internal DynamicDrawStateKnowledge Dynamic { get; }
    internal bool SampleMaskWords { get; }
    internal BlendStateKnowledge GlobalBlend { get; }
    internal PipelineValues<BlendStateKnowledge> Blending { get; }

    #region Public API
    /// <summary>Derives without native queries; absent emitted-primitive metadata takes the conservative path.</summary>
    /// <remarks>Current dynamic values do not select relevance. Future dynamic selectors must declare
    /// their potential union before boundary entry and resolve operation groups before application.</remarks>
    internal static GraphicsStateRelevance Derive(RHIGraphicsPipelineDescription description, GraphicsCapabilities capabilities,
        GraphicsExecutableInterface? executable = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(capabilities);
        PipelineDescriptionValidation.Validate(description, capabilities);
        return new(description, capabilities, executable);
    }
    #endregion

    #region Private
    #region Derivation
    /// <summary>Builds relevance from validated immutable inputs without repeating validation or touching the shared cache.</summary>
    internal GraphicsStateRelevance(RHIGraphicsPipelineDescription description, GraphicsCapabilities caps,
        GraphicsExecutableInterface? executable)
    {
        var depth = description.DepthStencil;
        Depth = DepthStateKnowledge.DepthRange;
        if (description.Targets.HasDepth)
        {
            Depth |= DepthStateKnowledge.TestEnabled;
            if (depth.DepthTest) Depth |= DepthStateKnowledge.Comparison | DepthStateKnowledge.WriteEnabled;
        }
        if (description.Targets.HasStencil)
            Stencil = depth.StencilTest ? StencilStateKnowledge.All : StencilStateKnowledge.TestEnabled;

        var raster = description.Rasterizer;
        var primitives = ResolvePrimitives(description, executable);
        Rasterizer = RasterGroups(raster, primitives, caps);
        Dynamic = DynamicDrawStateKnowledge.Viewport;
        if (raster.Scissor) Dynamic |= DynamicDrawStateKnowledge.Scissor;

        var sampling = description.Sampling;
        Sampling = SamplingStateKnowledge.Multisample | SamplingStateKnowledge.SampleCoverageEnabled
            | SamplingStateKnowledge.SampleMask | SamplingStateKnowledge.SampleAlphaToCoverage
            | SamplingStateKnowledge.SampleAlphaToOne;
        if (sampling.CoverageEnabled) Sampling |= SamplingStateKnowledge.SampleCoverage;
        if (caps.SampleShading)
        {
            Sampling |= SamplingStateKnowledge.SampleShading;
            if (sampling.SampleShading) Sampling |= SamplingStateKnowledge.MinimumSampleShading;
        }
        SampleMaskWords = sampling.MaskEnabled;

        var assembly = description.Assembly;
        Assembly = PrimitiveAssemblyStateKnowledge.PrimitiveRestart;
        if (caps.FixedIndexRestart) Assembly |= PrimitiveAssemblyStateKnowledge.PrimitiveRestartFixedIndex;
        if (assembly.Restart && !assembly.FixedIndexRestart) Assembly |= PrimitiveAssemblyStateKnowledge.RestartIndex;
        if (assembly.Topology == PrimitiveTopology.Patches) Assembly |= PrimitiveAssemblyStateKnowledge.PatchVertices;

        var outputs = new BlendStateKnowledge[description.Targets.Colors.Count];
        for (int slot = 0; slot < outputs.Length; slot++)
        {
            // Slots are fragment output locations, not framebuffer attachment numbers.
            if (description.Targets.Colors[slot].Format is not { } format) continue;
            var blend = description.Blending[slot];
            outputs[slot] = BlendStateKnowledge.WriteMask;
            if (TargetFormatPolicy.IsInteger(format)) continue;
            outputs[slot] |= BlendStateKnowledge.Enabled;
            if (!blend.Enabled) continue;
            var payload = BlendStateKnowledge.Equations;
            bool rgb = PipelineBlendSemantics.UsesFactors(blend.RgbEquation);
            bool alpha = PipelineBlendSemantics.UsesFactors(blend.AlphaEquation);
            if (rgb || alpha) payload |= BlendStateKnowledge.Factors;
            if (caps.IndependentBlend) outputs[slot] |= payload;
            else GlobalBlend |= payload; // Global factor/equation calls write every native output slot.
            if (rgb && (PipelineBlendSemantics.UsesConstant(blend.SourceRgb) || PipelineBlendSemantics.UsesConstant(blend.DestinationRgb))
                || alpha && (PipelineBlendSemantics.UsesConstant(blend.SourceAlpha) || PipelineBlendSemantics.UsesConstant(blend.DestinationAlpha)))
                Dynamic |= DynamicDrawStateKnowledge.BlendConstant;
        }
        Blending = new(outputs);
    }

    /// <summary>Uses existing verified tessellation metadata; geometry output is not currently retained.</summary>
    private static PrimitiveKinds ResolvePrimitives(RHIGraphicsPipelineDescription description, GraphicsExecutableInterface? executable)
    {
        var stages = description.Shader.Describe().Stages;
        if (stages.Any(s => s.Stage == ShaderStage.Geometry)) return PrimitiveKinds.All;
        bool tessellation = stages.Any(s => s.Stage == ShaderStage.TessellationEvaluation);
        if (tessellation && executable?.TessellationOutput is null) return PrimitiveKinds.All;
        var topology = tessellation ? executable!.TessellationOutput!.Value : description.Assembly.Topology;
        return topology switch
        {
            PrimitiveTopology.Points => PrimitiveKinds.Points,
            PrimitiveTopology.Lines or PrimitiveTopology.LineStrip or PrimitiveTopology.LineLoop
                or PrimitiveTopology.LinesAdjacency or PrimitiveTopology.LineStripAdjacency => PrimitiveKinds.Lines,
            PrimitiveTopology.Triangles or PrimitiveTopology.TriangleStrip or PrimitiveTopology.TriangleFan
                or PrimitiveTopology.TrianglesAdjacency or PrimitiveTopology.TriangleStripAdjacency => PrimitiveKinds.Polygons,
            _ => PrimitiveKinds.All
        };
    }

    /// <summary>Retains conservative controls and gates only proven dormant primitive-dependent native groups.</summary>
    private static RasterizerStateKnowledge RasterGroups(RasterizerDesc raster, PrimitiveKinds primitives,
        GraphicsCapabilities caps)
    {
        bool polygons = primitives.HasFlag(PrimitiveKinds.Polygons);
        bool lines = primitives.HasFlag(PrimitiveKinds.Lines) || polygons && raster.PolygonMode == RasterizerFillMode.Line;
        bool points = primitives.HasFlag(PrimitiveKinds.Points) || polygons && raster.PolygonMode == RasterizerFillMode.Point;
        var groups = RasterizerStateKnowledge.ScissorEnabled | RasterizerStateKnowledge.ProvokingVertex
            | RasterizerStateKnowledge.ClipDistances | RasterizerStateKnowledge.RasterizerDiscard;
        if (caps.ClipControl) groups |= RasterizerStateKnowledge.ClipControl;
        if (caps.DepthClamp) groups |= RasterizerStateKnowledge.DepthClamp;
        if (polygons)
        {
            groups |= RasterizerStateKnowledge.CullEnabled | RasterizerStateKnowledge.FrontFace
                | RasterizerStateKnowledge.PolygonModes;
            if (raster.PolygonMode == RasterizerFillMode.Fill) groups |= RasterizerStateKnowledge.PolygonSmooth;
            if (raster.Cull) groups |= RasterizerStateKnowledge.CullMode;
            var offset = raster.PolygonMode switch
            {
                RasterizerFillMode.Line => RasterizerStateKnowledge.PolygonOffsetLine,
                RasterizerFillMode.Point => RasterizerStateKnowledge.PolygonOffsetPoint,
                _ => RasterizerStateKnowledge.PolygonOffsetFill
            };
            groups |= offset;
            if (raster.PolygonMode == RasterizerFillMode.Fill && raster.OffsetFill
                || raster.PolygonMode == RasterizerFillMode.Line && raster.OffsetLine
                || raster.PolygonMode == RasterizerFillMode.Point && raster.OffsetPoint)
                groups |= RasterizerStateKnowledge.PolygonOffset;
        }
        if (lines)
        {
            groups |= RasterizerStateKnowledge.LineWidth | RasterizerStateKnowledge.LineSmooth;
        }
        if (points)
        {
            groups |= RasterizerStateKnowledge.ProgramPointSize | RasterizerStateKnowledge.PointSpriteOrigin;
            if (!raster.ProgramPointSize) groups |= RasterizerStateKnowledge.PointSize;
        }
        return groups;
    }

    #endregion
    #endregion

    /// <summary>Primitive families before polygon mode converts polygons into point or line rasterization.</summary>
    [Flags]
    private enum PrimitiveKinds
    {
        Points = 1,
        Lines = 2,
        Polygons = 4,
        All = Points | Lines | Polygons
    }
}
