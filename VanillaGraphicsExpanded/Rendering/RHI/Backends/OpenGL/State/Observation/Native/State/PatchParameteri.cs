using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes established glPatchParameteri effects through generated originating-context delivery.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Assigns the domain value after generated classification and targeted withdrawal.</summary>
    [InterceptOpenGL("glPatchParameteri", Withdraw = nameof(WithdrawPatchParameteri), Classify = nameof(ClassifyPatchParameteri))]
    internal static void ObservePatchParameteri(OpenGlStateCache cache, int parameter, int value)
    {
        cache.ObserveExternalPatchParameter((PatchParameterInt)parameter, value);
    }

    /// <summary>Rejects invalid selectors/counts and uses only already captured tessellation limits.</summary>
    internal static OpenGLObservedEffect ClassifyPatchParameteri(int parameter, int value)
    {
        if (parameter != (int)PatchParameterInt.PatchVertices || value <= 0) return OpenGLObservedEffect.Rejected;
        if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) return OpenGLObservedEffect.Uncertain;
        return value <= GpuSupport.Graphics.MaxPatchVertices ? OpenGLObservedEffect.Changed : OpenGLObservedEffect.Rejected;
    }

    /// <summary>Withdraws only this operation's affected knowledge after uncertainty or delivery failure.</summary>
    internal static void WithdrawPatchParameteri(OpenGlStateCache cache, int parameter, int value)
    {
        cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.PatchVertices);
    }
    #endregion
}
