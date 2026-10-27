using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains output-zero image intentions for repeated component draws.</summary>
internal static class PostprocessTestPasses
{
    private static readonly ConditionalWeakTable<GpuFramebuffer, RenderPassDesc> passes = new();

    #region Public API
    /// <summary>Uses a static owner's declaration or retains the fixture's fixed output-zero contract across repeated draws.</summary>
    internal static RenderPassDesc PostprocessPass(this GpuFramebuffer target)
    {
        if (target is StaticGpuFramebuffer immutable) return immutable.PreservePass;
        return passes.GetValue(target, static framebuffer => new RenderPassDesc(framebuffer, [new(0)]));
    }
    #endregion
}
