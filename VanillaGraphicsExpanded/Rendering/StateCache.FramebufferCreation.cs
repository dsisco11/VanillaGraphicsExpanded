using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Builds complete framebuffer creation descriptors from controlled inputs.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Realizes an empty object, applies optional defaults/selections and returns one complete candidate.</summary>
    internal GpuFramebufferMetadata? CreateFramebuffer(in GpuFramebufferInitialization initialization, out nint id)
    {
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess;
        int native;
        if (named) GL.CreateFramebuffers(1, out native);
        else native = GL.GenFramebuffer();
        id = native;
        if (native == 0) return null;
        InvalidateFramebufferDrawRouting(native);
        using var binding = named ? (FramebufferScope?)null : BindFramebufferScope(FramebufferTarget.Framebuffer, native);
        if (initialization.HasDefaults)
        {
            SetFramebufferCreationDefault(native, FramebufferDefaultParameter.FramebufferDefaultWidth, initialization.DefaultWidth);
            SetFramebufferCreationDefault(native, FramebufferDefaultParameter.FramebufferDefaultHeight, initialization.DefaultHeight);
            SetFramebufferCreationDefault(native, FramebufferDefaultParameter.FramebufferDefaultLayers, initialization.DefaultLayers);
            SetFramebufferCreationDefault(native, FramebufferDefaultParameter.FramebufferDefaultSamples, initialization.DefaultSamples);
            SetFramebufferCreationDefault(native, FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations, initialization.DefaultFixedSampleLocations ? 1 : 0);
        }
        var candidate = new GpuFramebufferMetadata(ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>.Empty,
            initialization.DefaultWidth, initialization.DefaultHeight, initialization.DefaultLayers,
            initialization.DefaultSamples, initialization.DefaultFixedSampleLocations);
        foreach (var pair in initialization.Attachments)
        {
            DispatchFramebufferAttachment(native, pair.Key, pair.Value);
            candidate = WithFramebufferAttachment(candidate, pair.Key, pair.Value);
        }
        return candidate;
    }
    #endregion

    #region Private
    /// <summary>Dispatches one initialization default on the selected capability path.</summary>
    private static void SetFramebufferCreationDefault(int id, FramebufferDefaultParameter parameter, int value)
    {
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.NamedFramebufferParameter(id, parameter, value);
        else GL.FramebufferParameter(FramebufferTarget.Framebuffer, parameter, value);
    }
    #endregion
}
