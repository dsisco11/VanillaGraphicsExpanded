using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes named renderbuffer allocation with cached sample selection.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <summary>Replaces owner storage and publishes once after named allocation completes.</summary>
    public void AllocateRenderbufferStorage(GpuRenderbuffer owner, in RHIRenderbufferDescription description)
    {
        var request = LowerRenderbuffer(description);
        RequireContext();
        owner.RequireOriginalContext();
        request.Validate();
        owner.RequireMutableStorage();
        using var operation = new GpuResourceManager.MetadataOperation<GpuRenderbufferMetadata>(owner, GpuMetadataChange.Storage);
        var result = DefineRenderbuffer(owner.RenderbufferId, request, operation);
        RequireContext();
        operation.Commit(result);
    }
    /// <inheritdoc />
    public void ResizeRenderbuffer(GpuRenderbuffer owner, int width, int height)
    {
        RequireContext();
        var data = owner.MetadataSnapshot;
        AllocateRenderbufferStorage(owner, new() { Format = OpenGLImageValues.Format(data.Format), Width = width, Height = height, Samples = data.Samples });
    }
    #endregion

    #region Internal API
    /// <summary>Creates a realized candidate for private manager publication.</summary>
    public GpuRenderbufferMetadata? InitializeRenderbuffer(in GpuRenderbufferInitialization request, out nint id)
    {
        RequireContext();
        int name = 0;
        try { GL.CreateRenderbuffers(1, out name); }
        finally { id = name; }
        RequireContext();
        if (name == 0) return null;
        var guard = cache.EnterResourceOperation(new(GpuResourceKind.Renderbuffer, name));
        try { return DefineRenderbuffer(name, request, creationGuard: guard); }
        finally
        {
            if (guard.Deleted) id = 0;
            cache.ExitResourceOperation(guard);
        }
    }


    #endregion

    #region Private
    /// <summary>Rejects omitted required fields before lowering project-owned allocation intent.</summary>
    private static GpuRenderbufferInitialization LowerRenderbuffer(in RHIRenderbufferDescription request)
    {
        if (!request.Has(0) || !request.Has(1) || !request.Has(2)) throw new ArgumentException("Renderbuffer format and extent are required.");
        var result = new GpuRenderbufferInitialization { Format = (RenderbufferStorage)OpenGLImageValues.Format(request.Format), Width = request.Width, Height = request.Height };
        if (request.Has(3)) result.Samples = request.Samples;
        if (request.Has(4)) result.CoverageSamples = request.CoverageSamples;
        result.Validate();
        return result;
    }
    /// <summary>Resolves samples from cached format capabilities, never a whole-descriptor query.</summary>
    private GpuRenderbufferMetadata DefineRenderbuffer(int id, in GpuRenderbufferInitialization request,
        GpuResourceManager.MetadataOperation<GpuRenderbufferMetadata>? operation = null,
        OpenGlStateCache.ResourceOperationGuard? creationGuard = null)
    {
        int samples = request.Samples;
        if (request.HasCoverageSamples)
            GL.Ext.NamedRenderbufferStorageMultisampleCoverage(id, request.CoverageSamples, samples, (Native.InternalFormat)request.Format, request.Width, request.Height);
        else if (samples > 0)
        {
            var support = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, (SizedInternalFormat)request.Format);
            if (!support.Supported) throw new ArgumentOutOfRangeException(nameof(request));
            int selected = support.SampleCounts.IsDefaultOrEmpty ? samples : int.MaxValue;
            foreach (int count in support.SampleCounts)
                if (count >= samples && count < selected) selected = count;
            if (!support.Supported || selected == int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(request), "No supported sample count satisfies the requested format.");
            samples = selected;
            GL.NamedRenderbufferStorageMultisample(id, samples, (Native.RenderbufferStorage)request.Format, request.Width, request.Height);
        }
        else GL.NamedRenderbufferStorage(id, (Native.RenderbufferStorage)request.Format, request.Width, request.Height);
        CheckRenderbufferStep(operation, creationGuard);
        if (request.HasCoverageSamples || samples > 0 && GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, (SizedInternalFormat)request.Format).SampleCounts.IsDefaultOrEmpty)
        {
            GL.GetNamedRenderbufferParameter(id, Native.RenderbufferParameterName.RenderbufferSamples, out samples);
            CheckRenderbufferStep(operation, creationGuard);
            if (samples <= 0) throw new InvalidOperationException("Allocated renderbuffer samples are unavailable.");
        }
        return new(request.Width, request.Height, (PixelInternalFormat)request.Format, samples);
    }

    /// <summary>Stops allocation-result reads after original identity or context retirement.</summary>
    private void CheckRenderbufferStep(GpuResourceManager.MetadataOperation<GpuRenderbufferMetadata>? operation,
        OpenGlStateCache.ResourceOperationGuard? creationGuard)
    {
        RequireContext();
        operation?.RequireLive();
        if (creationGuard?.Cancelled == true) throw new InvalidOperationException("Renderbuffer allocation retired during initialization.");
    }
    #endregion
}
