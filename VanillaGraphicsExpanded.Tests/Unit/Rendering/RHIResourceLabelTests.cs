using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks wrapper label declarations avoid native selectors and validity queries.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIResourceLabelTests
{
    #region Public API
    /// <summary>A diagnostic entry point never asks GL to verify an already admitted allocation.</summary>
    [Fact]
    public void LabelImplementationContainsNoNativeValidityQueries()
    {
        // Arrange: inspect the emitted method bodies for the managed label entry point.
        var method = typeof(GlDebug).GetMethod(nameof(GlDebug.TryLabel))!;
        var calls = PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>();

        // Act: inspect the managed label entry point's original instruction list.
        // Assert: no native validity query or pending-error read is part of the label contract.
        Assert.DoesNotContain(calls, m => m.DeclaringType?.Namespace?.StartsWith("OpenTK.Graphics") == true
            && (m.Name.StartsWith("Is", StringComparison.Ordinal) || m.Name == "GetError"));
#if !DEBUG
        Assert.DoesNotContain(calls, m => m.Name == "ObjectLabel");
        var backend = typeof(OpenGLBackend).GetMethod(nameof(OpenGLBackend.LabelResource))!;
        Assert.DoesNotContain(PatchProcessor.GetOriginalInstructions(backend), i => i.operand is MethodInfo);
#endif
    }

    /// <summary>Wrapper label methods retain managed names and route diagnostics through the owner boundary.</summary>
    [Fact]
    public void WrapperLabelsExposeNoNativeSelectors()
    {
        // Arrange: enumerate wrappers whose label setters must remain independent of native selector state.
        Type[] wrappers = [typeof(GpuBufferObject), typeof(GpuBufferView), typeof(GpuFramebuffer),
            typeof(GpuProgramObject), typeof(GpuProgramPipeline), typeof(GpuQuery), typeof(GpuRenderbuffer),
            typeof(GpuSampler), typeof(GpuShaderModule), typeof(GpuTexture), typeof(GpuTextureView), typeof(GpuVao)];

        // Act: inspect each wrapper's label setter instruction list.
        foreach (var wrapper in wrappers)
        {
            var method = wrapper.GetMethod(nameof(GpuResource.SetDebugName))!;
            var calls = PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>().ToArray();
            // Assert: wrapper setters expose no native selectors and retain the debug-only owner boundary.
            Assert.DoesNotContain(calls, m => m.DeclaringType == typeof(GlDebug)
                || m.DeclaringType?.Namespace?.StartsWith("OpenTK.Graphics") == true);
#if DEBUG
            Assert.Contains(calls, m => m.DeclaringType == typeof(RHICommandContext) && m.Name == "TryLabelResource");
#else
            Assert.DoesNotContain(calls, m => m.DeclaringType == typeof(RHICommandContext));
#endif
        }
    }
    #endregion
}
