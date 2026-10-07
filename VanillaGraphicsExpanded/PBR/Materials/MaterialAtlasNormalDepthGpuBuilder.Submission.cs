using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Coordinates bake passes with complete pipeline state and one independent restoration boundary per entry.</summary>
internal static partial class MaterialAtlasNormalDepthGpuBuilder
{
    private static readonly GraphicsPipelineLifetime PipelineLifetime = new();
    private static readonly Dictionary<PbrHeightBakeShaderProgram, GraphicsPipeline> Pipelines = new();

    #region Private
    /// <summary>Prepares the finite solver interfaces before entering the shared allocation, readback and draw boundary.</summary>
    private static bool RunBake(int destination, Action<BakeDrawContext> operation)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        using var image = GpuFramebufferAttachment.FromTextureId(destination);
        PbrHeightBakeShaderProgram[] programs = [progLuminance!, progGauss1D!, progSub!, progCombine!,
            progGradient!, progDivergence!, progJacobi!, progResidual!, progRestrict!, progProlongateAdd!,
            progNormalize!, progPackToAtlas!, progCopy!];
        foreach (var shader in programs)
        {
            if (!shader.EnsureReady()) return false;
            var format = ReferenceEquals(shader, progPackToAtlas) ? image.InternalFormat
                : ReferenceEquals(shader, progGradient) ? PixelInternalFormat.Rg32f : PixelInternalFormat.R32f;
            var target = new RenderTargetSignature([new(format)]);
            if (Pipelines.TryGetValue(shader, out var current) && current.ExecutableRevision == shader.ExecutableRevision
                && current.Description.Targets == target) continue;
            var replacement = new GraphicsPipeline(PipelineLifetime,
                new(shader.GraphicsIdentity!, new([]), target, DynamicPipelineState.Viewport), shader);
            current?.Dispose();
            Pipelines[shader] = replacement;
        }
        return GraphicsCommandContext.TryRun("MaterialAtlas.NormalDepthBake", programs.Select(p => Pipelines[p]).ToArray(), true,
            commands => operation(new(commands)));
    }
    #endregion

    /// <summary>Retains pass intentions between solver operations without copying native state or shader ownership.</summary>
    private sealed class BakeDrawContext(GraphicsCommandContext commands)
    {
        private RenderArea area;
        private static readonly RenderPassColor[] PreserveOutput = [new(0)];

        #region Public API
        /// <summary>Retargets the owned scratch FBO between completed passes to one solver image.</summary>
        internal void SetTarget(DynamicTexture2D image)
        {
            scratchFbo!.Attach(image);
            area = new(0, 0, image.Width, image.Height);
        }

        /// <summary>Borrows the atlas image and restricts the draw viewport to the requested rectangle.</summary>
        internal void SetAtlasTarget(int texture, int x, int y, int width, int height)
        {
            scratchFbo!.Attach(texture);
            area = new(x, y, width, height);
        }

        /// <summary>Publishes current shared UBO inputs before drawing a bounded procedural triangle.</summary>
        internal void Draw(PbrHeightBakeShaderProgram shader)
        {
            commands.BeginPass(new(scratchFbo!, PreserveOutput, area: area));
            commands.SetPipeline(Pipelines[shader]);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry!, new(0, 3));
            commands.EndPass();
        }

        /// <summary>Clears only the selected area with load operations independent of inherited write masks.</summary>
        internal void Clear(float r, float g, float b, float a)
        {
            commands.BeginPass(new(scratchFbo!, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(r, g, b, a))], area: area));
            commands.EndPass();
        }
        #endregion
    }
}
