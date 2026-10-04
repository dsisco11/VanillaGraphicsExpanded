using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Isolates ordered particle radiance while borrowing the engine's visibility depth and glow images.</summary>
internal sealed class SceneColorParticleTargets : IDisposable
{
    private static readonly GlPipelineDesc CopyPipeline = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.ScissorTestEnable),
        nonDefaultMask: default, name: "SceneColor.Particles.DepthCopy");
    private readonly List<GpuResource> ownedImages = new();
    private readonly GpuFramebuffer primary;
    private GpuFramebuffer? beforeTarget;
    private GpuFramebuffer? afterTarget;
    private GpuFramebufferBlitter? beforeCopy;
    private GpuFramebufferBlitter? afterCopy;
    private bool captureStarted;
    private bool disposed;

    #region Public API
    /// <summary>Gets particle radiance, borrowed glow and optional isolated SSAO outputs.</summary>
    internal GpuFramebuffer DrawTarget { get; private set; } = null!;
    /// <summary>Gets the resolve destination for material receiver depth and visible premultiplied particles.</summary>
    internal GpuFramebuffer ResolveTarget { get; private set; } = null!;
    /// <summary>Gets the borrowed engine SSAO output destination when those attachments exist.</summary>
    internal GpuFramebuffer? SsaoTarget { get; private set; }
    /// <summary>Reports whether the original particle SSAO outputs have isolated storage.</summary>
    internal bool HasSsao => SsaoTarget is not null;
    /// <summary>Gets straight-alpha-blended radiance, with accumulated coverage in alpha.</summary>
    internal int ParticleColor => DrawTarget.GetColorTextureId(0);
    /// <summary>Gets the depth before the original particle callback.</summary>
    internal int BeforeDepth => beforeTarget?.GetDepthTextureId() ?? 0;
    /// <summary>Gets the engine depth immediately after the original particle callback.</summary>
    internal int AfterDepth => afterTarget?.GetDepthTextureId() ?? 0;
    /// <summary>Gets the depth image used by the engine for particle visibility and later opaque draws.</summary>
    internal int VisibilityDepth => DrawTarget.GetDepthTextureId();
    /// <summary>Reports a completed pair of depth snapshots from one particle invocation.</summary>
    internal bool Captured { get; private set; }
    /// <summary>Reports whether the borrowed primary publication still matches this allocation.</summary>
    internal bool IsCurrent { get; private set; }

    /// <summary>Allocates independent snapshots and borrows primary images once at their publication boundary.</summary>
    internal SceneColorParticleTargets(GpuFramebuffer primary, bool captureSsao = false)
    {
        this.primary = primary;
        try
        {
            var depth = GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferAttachment.DepthAttachment);
            var glow = GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferAttachment.ColorAttachment1);
            // Exact copies let the resolve identify surviving particle depth without
            // guessing a tolerance that could include a later foreground surface.
            if (depth.InternalFormat is not (PixelInternalFormat.DepthComponent32 or PixelInternalFormat.DepthComponent32f) || depth.Samples != 0
                || depth.TextureId == 0 || glow.Width != depth.Width || glow.Height != depth.Height)
                throw new InvalidOperationException("Particle capture requires matching single-sample primary 32-bit depth and glow images.");
            var color = Allocate(PixelInternalFormat.Rgba16f, "Radiance");
            var before = Allocate(depth.InternalFormat, "BeforeDepth");
            var after = Allocate(depth.InternalFormat, "AfterDepth");
            var receiver = Allocate(PixelInternalFormat.R32f, "ReceiverDepth");
            var visible = Allocate(PixelInternalFormat.Rgba16f, "VisibleRadiance");
            if (captureSsao)
            {
                var normal = Allocate(PixelInternalFormat.Rgba16f, "SsaoNormal");
                var position = Allocate(PixelInternalFormat.Rgba16f, "SsaoPosition");
                DrawTarget = GpuFramebuffer.Create([color, glow, normal, position], depth, "SceneColor.Particles.Draw");
                // Deferred lighting keeps the original position attachment until
                // composition finishes. Only then are surviving particle values restored.
                SsaoTarget = GpuFramebuffer.Create([
                    GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferAttachment.ColorAttachment2),
                    GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferAttachment.ColorAttachment3)],
                    debugName: "SceneColor.Particles.SsaoRestore");
            }
            else DrawTarget = GpuFramebuffer.Create([color, glow], depth, "SceneColor.Particles.Draw");
            beforeTarget = GpuFramebuffer.Create([], before, "SceneColor.Particles.Before");
            afterTarget = GpuFramebuffer.Create([], after, "SceneColor.Particles.After");
            ResolveTarget = GpuFramebuffer.Create([receiver, visible], debugName: "SceneColor.Particles.Resolve");
            beforeCopy = new(primary, beforeTarget, ClearBufferMask.DepthBufferBit);
            afterCopy = new(primary, afterTarget, ClearBufferMask.DepthBufferBit);
            primary.AttachmentsChanged += Invalidate;
            IsCurrent = true;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Clears owned radiance and metadata and records underlying depth, preserving bindings and fixed-function state.</summary>
    internal void BeginCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrent) throw new InvalidOperationException("Particle capture belongs to an expired primary publication.");
        Captured = false;
        captureStarted = false;
        CopyDepth(beforeCopy!);
        // Clear storage directly so indexed write masks and borrowed glow/depth
        // remain untouched. Unsupported setup cannot publish an HDR capture.
        if (!DrawTarget[0].TryClearToZero())
            throw new InvalidOperationException("Particle radiance clear is unavailable.");
        if (HasSsao && (!DrawTarget[2].TryClearToZero() || !DrawTarget[3].TryClearToZero()))
            throw new InvalidOperationException("Particle SSAO clear is unavailable.");
        captureStarted = true;
    }

    /// <summary>Records visibility after the unchanged particle callback completes successfully.</summary>
    internal void EndCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrent || !captureStarted)
            throw new InvalidOperationException("Particle capture has no matching current-frame start.");
        captureStarted = false;
        CopyDepth(afterCopy!);
        Captured = true;
    }

    /// <summary>Withdraws capture on failure or a new frame without reallocating its images.</summary>
    internal void ResetCapture()
    {
        Captured = false;
        captureStarted = false;
    }

    /// <summary>Unsubscribes and retires only owned images and FBOs, leaving primary depth and glow alive.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        primary.AttachmentsChanged -= Invalidate;
        Invalidate();
        beforeCopy?.Dispose();
        afterCopy?.Dispose();
        DrawTarget?.Dispose();
        ResolveTarget?.Dispose();
        SsaoTarget?.Dispose();
        beforeTarget?.Dispose();
        afterTarget?.Dispose();
        foreach (var image in ownedImages) image.Dispose();
        ownedImages.Clear();
    }
    #endregion

    #region Private
    /// <summary>Copies the whole depth image without disturbing indexed G-buffer blend or write masks.</summary>
    private static void CopyDepth(GpuFramebufferBlitter copy)
    {
        var state = StateCache.Current;
        using var scissor = state.PreserveScissorState();
        // Describe only the state this copy owns; a broad legacy restoration
        // would overwrite unrelated indexed blend settings on the G-buffer.
        state.Apply(CopyPipeline);
        copy.Blit();
    }

    /// <summary>Tracks owned allocations immediately so partial setup failures cannot leak storage.</summary>
    private GpuFramebufferAttachment Allocate(PixelInternalFormat format, string name)
    {
        if (format is PixelInternalFormat.DepthComponent32 or PixelInternalFormat.DepthComponent32f)
        {
            var depth = new DepthTexture(primary.Width, primary.Height, format,
                debugName: "SceneColor.Particles." + name);
            ownedImages.Add(depth);
            return GpuFramebufferAttachment.FromTexture(depth);
        }
        var image = new GpuFramebufferAttachment(primary.Width, primary.Height, format,
            debugName: "SceneColor.Particles." + name);
        ownedImages.Add(image);
        return image;
    }

    /// <summary>Rejects every stale capture when the engine replaces or retires its primary images.</summary>
    private void Invalidate()
    {
        IsCurrent = false;
        ResetCapture();
    }
    #endregion
}
