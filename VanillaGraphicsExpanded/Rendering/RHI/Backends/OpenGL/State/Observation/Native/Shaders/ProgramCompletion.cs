using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Completes only an observed pending executable when its caller obtains native completion output.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Consumes core link status or a supported positive parallel-completion flag.</summary>
    [InterceptOpenGL("glGetProgramiv")]
    internal static void ObserveProgramCompletion(uint program, int parameter, int* result)
    {
        if (result == null) return;
        if (parameter != (int)GetProgramParameterName.LinkStatus)
        {
            if (parameter != (int)ArbParallelShaderCompile.CompletionStatusArb || *result == 0
                || !GpuSupport.HasCachedCapabilitiesForCurrentContext
                || !GpuSupport.Graphics.SupportsArbParallelShaderCompile && !GpuSupport.Graphics.SupportsKhrParallelShaderCompile) return;
        }
        CompleteObservedProgram(program);
    }

    /// <summary>Consumes the ARB integer object's guaranteed link-status output without inferring unsupported tokens.</summary>
    [InterceptOpenGL("glGetObjectParameterivARB")]
    internal static void ObserveObjectProgramCompletion(uint program, int parameter, int* result)
    {
        if (result != null && parameter == (int)GetProgramParameterName.LinkStatus) CompleteObservedProgram(program);
    }

    /// <summary>Consumes the ARB floating-point object's equivalent link-status output.</summary>
    [InterceptOpenGL("glGetObjectParameterfvARB")]
    internal static void ObserveObjectProgramCompletionFloat(uint program, int parameter, float* result)
    {
        if (result != null && parameter == (int)GetProgramParameterName.LinkStatus) CompleteObservedProgram(program);
    }
    #endregion

    #region Private
    /// <summary>Publishes only externally pending output; native queries cannot take over an owned RHI candidate.</summary>
    private static void CompleteObservedProgram(uint program)
    {
        if (!OpenGLObservationContext.TryGet(out _, out _, false) || !GpuSupport.HasCachedCapabilitiesForCurrentContext) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        if (manager.FindObservedResource(GpuResourceKind.Program, unchecked((int)program))
            is not GpuResourceMetadataEntry<GpuProgramMetadata> entry
            || entry.IsUsable || entry.OperationInProgress || entry.CompletionInProgress || !entry.CompletionInterrupted
            || (entry.PendingChange & GpuMetadataChange.Executable) == 0) return;
        entry.Reader ??= GpuProgramMetadataReader.Instance;
        manager.RefreshMetadata(entry, GpuMetadataChange.Executable | GpuMetadataChange.Metadata);
    }
    #endregion
}
