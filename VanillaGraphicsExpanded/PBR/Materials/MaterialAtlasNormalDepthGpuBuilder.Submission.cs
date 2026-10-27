using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    private static readonly Dictionary<PbrHeightBakeShaderProgram, GraphicsPipelineAsset> Pipelines = new();

    #region Private
    /// <summary>Prepares the finite solver interfaces before entering the shared allocation, readback and draw boundary.</summary>
    private static bool RunBake(GpuTextureRef destination, Action<BakeDrawContext> operation)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        var imageFormat=RHICommandContext.Current.DescribeRenderTargets(new([new(Image:RHIAttachment.FromTexture(destination))])).Colors[0].Format!.Value;
        PbrHeightBakeShaderProgram[] programs = [progLuminance!, progGauss1D!, progSub!, progCombine!,
            progGradient!, progDivergence!, progJacobi!, progResidual!, progRestrict!, progProlongateAdd!,
            progNormalize!, progPackToAtlas!, progCopy!];
        foreach (var shader in programs)
        {
            if (!shader.EnsureReady()) return false;
            var format = ReferenceEquals(shader, progPackToAtlas) ? imageFormat
                : ReferenceEquals(shader, progGradient) ? TextureFormat.Rg32f : TextureFormat.R32f;
            var target = new RenderTargetSignature([new(format)]);
            if (Pipelines.TryGetValue(shader, out var current) && current.Description.Shader == shader.Shader
                && current.Description.Targets == target) continue;
            var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, new([]), target, DynamicPipelineState.Viewport));
            current?.Dispose();
            Pipelines[shader] = replacement;
        }
        var commands=RHICommandContext.Current;
        if(!commands.TryEnterBoundary(new("MaterialAtlas.NormalDepthBake",true,graphics:programs.Select(program=>(RHIGraphicsPipelineRef)Pipelines[program].Pipeline),operations:BoundaryOperations.RenderPasses),out var boundary)) return false;
        using(boundary) boundary!.Run(()=>operation(new(commands)));
        return true;
    }
    #endregion

    /// <summary>Retains pass intentions between solver operations without copying native state or shader ownership.</summary>
    private sealed class BakeDrawContext(RHICommandContext commands)
    {
        private RenderArea area;
        private GpuTextureRef selected;

        #region Public API
        /// <summary>Selects one retained solver image for subsequent pure attachment intentions.</summary>
        internal void SetTarget(DynamicTexture2D image)
        {
            selected=image;
            area = new(0, 0, image.Width, image.Height);
        }

        /// <summary>Borrows the atlas image and restricts the draw viewport to the requested rectangle.</summary>
        internal void SetAtlasTarget(GpuTextureRef texture, int x, int y, int width, int height)
        {
            selected=texture;
            area = new(x, y, width, height);
        }

        /// <summary>Publishes current shared UBO inputs before drawing a bounded procedural triangle.</summary>
        internal void Draw(PbrHeightBakeShaderProgram shader)
        {
            commands.BeginRenderPass(new([new(Image:RHIAttachment.FromTexture(selected))],area:area));
            commands.SetPipeline(Pipelines[shader].Pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            commands.Draw(geometry!.Inputs,new(0,3));
            commands.EndRenderPass();
        }

        /// <summary>Clears only the selected area with load operations independent of inherited write masks.</summary>
        internal void Clear(float r, float g, float b, float a)
        {
            commands.BeginRenderPass(new([new(Image:RHIAttachment.FromTexture(selected),Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(r,g,b,a))],area:area));
            commands.EndRenderPass();
        }
        #endregion
    }
}
