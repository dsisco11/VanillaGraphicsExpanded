using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes renderbuffer allocation and publishes established storage facts.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Creates private storage for the manager's initial publication and ownership handoff.</summary>
    internal GpuRenderbufferMetadata? CreateRenderbuffer(in GpuRenderbufferInitialization request, out nint id)
    {
        id = 0;
        int samples = ResolveRenderbufferSamples(request.Format, request.Samples);
        int name;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.CreateRenderbuffers(1, out name);
        else name = GL.GenRenderbuffer();
        id = name;
        if (name == 0) return null;
        return AllocateRenderbuffer(name, request.Format, request.Width, request.Height, samples);
    }

    /// <summary>Publishes supplied dimensions and format with a capability-resolved sample count.</summary>
    internal void RenderbufferStorage(GpuRenderbuffer resource, RenderbufferStorage storage, int width, int height, int samples)
    {
        var request = new GpuRenderbufferInitialization { Format = storage, Width = width, Height = height, Samples = samples };
        request.Validate();
        samples = ResolveRenderbufferSamples(storage, samples);
        using var operation = new GpuResourceManager.MetadataOperation<GpuRenderbufferMetadata>(resource, GpuMetadataChange.Storage);
        operation.Commit(AllocateRenderbuffer(resource.RenderbufferId, storage, width, height, samples));
    }
    #endregion

    #region Private
    /// <summary>Normalizes requested samples once from cached target-specific capabilities before allocation.</summary>
    private static int ResolveRenderbufferSamples(RenderbufferStorage storage, int requested)
    {
        if (requested == 0 || !GpuSupport.Graphics.SupportsInternalFormatQuery2) return requested;
        var support = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, (SizedInternalFormat)storage);
        int selected = int.MaxValue;
        foreach (int count in support.SampleCounts)
            if (count >= requested && count < selected) selected = count;
        if (!support.Supported || selected == int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(requested), "The format has no supported sample count satisfying this request.");
        return selected;
    }

    /// <summary>Executes storage once; only sample counts lacking capability data require a result query.</summary>
    private GpuRenderbufferMetadata AllocateRenderbuffer(int id, RenderbufferStorage storage, int width, int height, int samples)
    {
        using var errors = new GlDebug.ErrorScope("Renderbuffer storage allocation");
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            if (samples > 0) GL.NamedRenderbufferStorageMultisample(id, samples, storage, width, height);
            else GL.NamedRenderbufferStorage(id, storage, width, height);
        }
        else
        {
            using var binding = BindRenderbufferScope(id);
            if (samples > 0) GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, samples, storage, width, height);
            else GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, storage, width, height);
        }
        GlDebug.ThrowIfErrors("Renderbuffer storage allocation");
        if (samples > 0 && !GpuSupport.Graphics.SupportsInternalFormatQuery2)
            samples = GpuRenderbufferMetadataReader.ReadSamples(id);
        return new(width, height, (PixelInternalFormat)storage, samples);
    }
    #endregion
}