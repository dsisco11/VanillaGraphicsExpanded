using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns a completed private framebuffer realization without freezing or owning its attachment storage.</summary>
internal sealed class OpenGLPassTarget : GpuFramebuffer
{
    private readonly OpenGlStateCache configurationOwner = OpenGlStateCache.Current;
    private readonly DrawBuffersEnum[] route;
    private readonly GpuResourceRegistration? registration;
    private readonly ulong metadataRevision, attachmentRevision;
    private readonly GpuFramebufferAttachment[] projections = [];

    #region Public API
    /// <inheritdoc />
    public override void SetAttachment(FramebufferSlot slot, GpuFramebufferAttachment image)
        => throw new InvalidOperationException("Replace a backend target realization to change its configuration.");
    /// <inheritdoc />
    public override bool RemoveAttachment(FramebufferSlot slot)
        => throw new InvalidOperationException("A backend target configuration is immutable.");
    /// <inheritdoc />
    public override bool Resize(int width, int height)
        => throw new InvalidOperationException("Resize attachment storage through its original owner.");
    #endregion

    #region Internal API
    /// <summary>Creates from resolved canonical images; the backend completes routing and checks completeness before publication.</summary>
    internal OpenGLPassTarget(OpenGLBackend backend, in GpuFramebufferInitialization request, DrawBuffersEnum[] route)
        : base(true, name: "RHI.PassTarget", initialization: request)
    {
        this.route = route;
        projections = request.Attachments.Values.Distinct().ToArray();
        try
        {
            backend.CompletePassTarget(this, route);
            registration = ResourceManager?.GetResourceRegistration(this);
            metadataRevision = registration?.MetadataRevision ?? 0;
            attachmentRevision = AttachmentRevision;
            configurationOwner.RegisterImmutableFramebuffer(this);
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Rejects changed native configuration or storage dependencies using existing publication authority.</summary>
    internal bool IsReusable => IsValid && AttachmentRevision == attachmentRevision
        && ReferenceEquals(registration, ResourceManager?.GetResourceRegistration(this))
        && (registration is null || registration.IsUsable && registration.MetadataRevision == metadataRevision);
    /// <summary>Returns the fixed native route independently of mutable selector knowledge.</summary>
    internal override int[] CaptureDeclaredRouting() => System.Array.ConvertAll(route, value => value == DrawBuffersEnum.None ? -1 : (int)value - (int)DrawBuffersEnum.ColorAttachment0);
    /// <summary>Withdraws configuration authority without issuing GL when its original context retires.</summary>
    internal void WithdrawConfiguration() => configurationOwner.UnregisterImmutableFramebuffer(FboId, this);
    /// <inheritdoc />
    internal override void ValidateDeclaredRouting(ReadOnlySpan<int> values)
    {
        for(int i=0;i<Math.Max(values.Length,route.Length);i++)
            if((i<values.Length?values[i]:-1)!=(i<route.Length && route[i] != DrawBuffersEnum.None ? (int)route[i] - (int)DrawBuffersEnum.ColorAttachment0 : -1))
                throw new InvalidOperationException("Pass routing differs from its realized target.");
    }
    #endregion

    #region Protected API
    /// <summary>Withdraws immutable admission before native deletion enters the existing retirement service.</summary>
    protected override void OnBeforeDelete(nint id)
    {
        configurationOwner.UnregisterImmutableFramebuffer((int)id, this);
        base.OnBeforeDelete(id);
    }
    /// <summary>Releases selected-image recipients after target retirement without disposing independently owned backing storage.</summary>
    protected override void OnAfterDelete()
    {
        base.OnAfterDelete();
        foreach (var image in projections) image.Dispose();
    }
    #endregion
}
