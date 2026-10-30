using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates framebuffer sampling against the pipeline that will actually execute the draw.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Rejects writable overlap without inferring synchronization from resource bindings.</summary>
    private static void ValidateGraphicsFeedback(RHIGraphicsPipeline pipeline, RHIResourceBindings? resources)
    {
        if (resources is null) return;
        // Only executable sampler entries participate; permutation-excluded inputs cannot sample storage.
        foreach (var active in pipeline.RequireShader().Interface.PreparedBindings.ActiveEntries)
        {
            if (active.Binding.Contract.Kind != ShaderBindingKind.Sampler
                || !resources.TrySampler(active.Identity, active.Element, out var input)
                || input.Texture.IsEmpty) continue;
            RenderPass.ValidateSampledTexture(input.Texture, pipeline.Description.DepthStencil);
        }
    }
    #endregion
}
