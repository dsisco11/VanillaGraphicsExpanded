using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates framebuffer sampling against the pipeline that will actually execute the draw.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Rejects writable overlap and requests visibility ordering for attached read-only depth.</summary>
    private static bool ValidateGraphicsFeedback(RHIGraphicsPipeline pipeline, RHIResourceBindings? resources)
    {
        if (resources is null) return false;
        bool needsBarrier = false;
        // Only executable sampler entries participate; permutation-excluded inputs cannot sample storage.
        foreach (var active in pipeline.RequireShader().Interface.PreparedBindings.ActiveEntries)
        {
            if (active.Binding.Contract.Kind != ShaderBindingKind.Sampler
                || !resources.TrySampler(active.Identity, active.Element, out var input)
                || input.Texture.IsEmpty) continue;
            needsBarrier |= RenderPass.ValidateSampledTexture(input.Texture, pipeline.Description.DepthStencil);
        }
        return needsBarrier;
    }
    #endregion
}
