using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains output-zero image intentions for repeated component draws.</summary>
internal static class PostprocessTestPasses
{
    private static readonly ConditionalWeakTable<GpuFramebuffer, RHIRenderPassDescription> passes = new();
    #region Internal API
    /// <summary>Reuses immutable intentions while the backend validates current original storage at execution.</summary>
    internal static RHIRenderPassDescription PostprocessPass(this GpuFramebuffer target)
        => passes.GetValue(target, framebuffer => FramebufferTestSetup.DescribeAttachments(framebuffer));
    #endregion
}
