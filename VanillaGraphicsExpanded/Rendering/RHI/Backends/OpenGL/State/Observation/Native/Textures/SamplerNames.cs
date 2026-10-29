namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Tracks sampler identity and specified first-use initialization without a parameter mirror.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains generated reservations until a specified sampler use establishes defaults.</summary>
    [InterceptOpenGL("glGenSamplers")]
    internal static void ObserveGenSamplers(int count, uint* names) => ObserveSamplerNames(count, names, false);

    /// <summary>Publishes immediately realized core sampler identities.</summary>
    [InterceptOpenGL("glCreateSamplers")]
    internal static void ObserveCreateSamplers(int count, uint* names) => ObserveSamplerNames(count, names, true);

    /// <summary>Consumes explicit validity output, which itself initializes a generated sampler.</summary>
    [InterceptOpenGL("glIsSampler")]
    internal static void ObserveIsSampler(uint name, byte result)
    {
        if (result != 0) RealizeObservedSampler(unchecked((int)name));
    }

    /// <summary>Initializes an original reservation independently of parameter acceptance; values are not canonical fields.</summary>
    [InterceptOpenGL("glSamplerParameteri")]
    internal static void ObserveSamplerParameteri(uint name, int parameter, int value) => RealizeObservedSampler(unchecked((int)name));

    /// <summary>Initializes a reserved sampler on floating parameter access.</summary>
    [InterceptOpenGL("glSamplerParameterf")]
    internal static void ObserveSamplerParameterf(uint name, int parameter, float value) => RealizeObservedSampler(unchecked((int)name));

    /// <summary>Initializes a reserved sampler on signed parameter access without reading parameter values.</summary>
    [InterceptOpenGL("glSamplerParameteriv")]
    [InterceptOpenGL("glSamplerParameterIiv")]
    [InterceptOpenGL("glGetSamplerParameteriv")]
    [InterceptOpenGL("glGetSamplerParameterIiv")]
    internal static void ObserveSamplerParameterIntegers(uint name, int parameter, int* values) => RealizeObservedSampler(unchecked((int)name));

    /// <summary>Initializes a reserved sampler on unsigned integer parameter access.</summary>
    [InterceptOpenGL("glSamplerParameterIuiv")]
    [InterceptOpenGL("glGetSamplerParameterIuiv")]
    internal static void ObserveSamplerParameterUnsigned(uint name, int parameter, uint* values) => RealizeObservedSampler(unchecked((int)name));

    /// <summary>Initializes a reserved sampler on floating parameter access.</summary>
    [InterceptOpenGL("glSamplerParameterfv")]
    [InterceptOpenGL("glGetSamplerParameterfv")]
    internal static void ObserveSamplerParameterFloats(uint name, int parameter, float* values) => RealizeObservedSampler(unchecked((int)name));
    /// <summary>Reconciles each sampler destination independently, including null-array unbinding.</summary>
    [InterceptOpenGL("glBindSamplers")]
    internal static void ObserveBindSamplers(uint first, int count, uint* names)
    {
        if (count <= 0 || !OpenGLObservationContext.TryGet(out var cache, out _)
            || (ulong)first + (uint)count > (uint)GpuSupport.MaxCombinedTextureImageUnits) return;
        for (int index = 0; index < count; index++)
        {
            int id = names == null ? 0 : unchecked((int)names[index]);
            if (id == 0 || RealizeObservedSampler(id)) cache.SetSamplerKnowledge((int)first + index, id);
            else cache.SetSamplerKnowledge((int)first + index, null);
        }
    }
    #endregion

    #region Private
    /// <summary>Associates defaults only with a known generated original, never an arbitrary numeric name.</summary>
    private static bool RealizeObservedSampler(int name)
    {
        return name != 0 && OpenGLObservationContext.TryGet(out _, out _, false)
            && RHIResourceManagerSystem.CurrentManager!.RealizeEmptyResource<GpuSamplerMetadata>(GpuResourceKind.Sampler, name);
    }

    /// <summary>Consumes factory arrays once and rejects invalid or already-live outputs.</summary>
    private static void ObserveSamplerNames(int count, uint* names, bool realized)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int index = 0; index < count; index++)
            manager.ObserveEmptyCreation<GpuSamplerMetadata>(GpuResourceKind.Sampler, unchecked((int)names[index]), realized);
    }
    #endregion
}
