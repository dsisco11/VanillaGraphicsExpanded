using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Identifies mutable state categories whose cached knowledge can be invalidated independently.</summary>
/// <remarks>These are invalidation categories, not the bit layout of a pipeline descriptor.</remarks>
[Flags]
public enum EPipelineState : ulong
{
    /// <summary>No state.</summary>
    None = 0,
    /// <summary>Depth-test enable, comparison and write mask.</summary>
    Depth = 1UL << 0,
    /// <summary>Global and indexed blending enables and factors.</summary>
    Blend = 1UL << 1,
    /// <summary>Face-culling enable.</summary>
    CullFace = 1UL << 2,
    /// <summary>Scissor-test enable.</summary>
    ScissorTest = 1UL << 3,
    /// <summary>Global color write mask.</summary>
    ColorMask = 1UL << 4,
    /// <summary>Line width.</summary>
    LineWidth = 1UL << 5,
    /// <summary>Point size.</summary>
    PointSize = 1UL << 6,
    /// <summary>Patch control-point count.</summary>
    PatchVertices = 1UL << 7,
    /// <summary>Provoking-vertex convention.</summary>
    ProvokingVertex = 1UL << 8,
    /// <summary>Current executable program.</summary>
    Program = 1UL << 9,
    /// <summary>Current separable program pipeline.</summary>
    ProgramPipeline = 1UL << 10,
    /// <summary>Current VAO and cached VAO-owned element-buffer associations.</summary>
    VertexArray = 1UL << 11,
    /// <summary>Read, draw and combined framebuffer binding snapshots.</summary>
    FramebufferBindings = 1UL << 12,
    /// <summary>Renderbuffer binding.</summary>
    RenderbufferBinding = 1UL << 13,
    /// <summary>Transform-feedback binding and its indexed buffer assignments.</summary>
    TransformFeedback = 1UL << 14,
    /// <summary>Active texture-unit selection, independently of per-unit bindings.</summary>
    ActiveTextureUnit = 1UL << 15,
    /// <summary>Per-unit texture bindings, independently of the active unit and samplers.</summary>
    TextureBindings = 1UL << 16,
    /// <summary>Per-unit sampler bindings.</summary>
    SamplerBindings = 1UL << 17,
    /// <summary>Generic, indexed and VAO-owned element-buffer bindings; preserves selected VAO.</summary>
    BufferBindings = 1UL << 18,
    /// <summary>Image-unit view assignments.</summary>
    ImageBindings = 1UL << 19,
    /// <summary>Pixel-pack layout.</summary>
    PixelPack = 1UL << 20,
    /// <summary>Dynamic viewport rectangle.</summary>
    Viewport = 1UL << 21,
    /// <summary>Resource clear-operation color.</summary>
    ClearColor = 1UL << 22,
    /// <summary>Configurable clipping, alpha, smoothing and stipple state.</summary>
    ConfigurableRaster = 1UL << 23,
    /// <summary>Pixel-unpack layout.</summary>
    PixelUnpack = 1UL << 24,
    /// <summary>Front/back stencil functions, operations, masks and enable.</summary>
    Stencil = 1UL << 25,
    /// <summary>Multisampling, coverage, masks and sample shading.</summary>
    Sampling = 1UL << 26,
    /// <summary>Framebuffer encoding, dithering and logic operations.</summary>
    OutputInterpretation = 1UL << 27,
    /// <summary>Explicit and fixed primitive restart configuration.</summary>
    PrimitiveRestart = 1UL << 28,
    /// <summary>Cull winding, polygon interpretation, offsets, clamp, discard and programmable points.</summary>
    RasterParameters = 1UL << 29,
    /// <summary>Dynamic scissor rectangle, independently of the test enable.</summary>
    ScissorRectangle = 1UL << 30,
    /// <summary>Dynamic blend constant.</summary>
    BlendConstant = 1UL << 31,
    /// <summary>Depth-range mapping.</summary>
    DepthRange = 1UL << 32,
    /// <summary>Supplemental complete-pipeline state categories.</summary>
    CompleteGraphics = Stencil | Sampling | OutputInterpretation | PrimitiveRestart | RasterParameters | ScissorRectangle | BlendConstant | DepthRange,
    /// <summary>All tracked fixed-function drawing state.</summary>
    FixedFunction = CompleteGraphics | ConfigurableRaster | Depth | Blend | CullFace | ScissorTest | ColorMask | LineWidth | PointSize | PatchVertices | ProvokingVertex,
    /// <summary>All tracked resource bindings and active texture-unit selection.</summary>
    Bindings = Program | ProgramPipeline | VertexArray | FramebufferBindings | RenderbufferBinding
        | TransformFeedback | ActiveTextureUnit | TextureBindings | SamplerBindings | BufferBindings | ImageBindings,
    /// <summary>All mutable state tracked by the cache.</summary>
    All = FixedFunction | Bindings | PixelUnpack | PixelPack | Viewport | ClearColor
}
