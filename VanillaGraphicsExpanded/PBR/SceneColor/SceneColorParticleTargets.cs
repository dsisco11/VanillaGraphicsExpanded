using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Isolates ordered particle radiance while borrowing the engine's visibility depth and glow images.</summary>
internal sealed class SceneColorParticleTargets : IDisposable
{
    private readonly List<GpuResource> ownedImages = new();
    private readonly GpuFramebuffer primary;
    private BorrowedTexture? visibilityDepthInput;
    private BorrowedTexture? normalOutput,positionOutput;
    private bool captureStarted;
    private bool disposed;

    #region Public API
    /// <summary>Gets particle radiance, borrowed glow and optional isolated SSAO outputs.</summary>
    internal GpuFramebuffer DrawTarget { get; private set; } = null!;
    /// <summary>Gets the resolve destination for material receiver depth and visible premultiplied particles.</summary>
    internal RHIRenderPassDescription ResolvePass { get; private set; } = null!;
    /// <summary>Gets owned corrected material depth after resolve.</summary>
    internal DynamicTexture2D MaterialDepth { get; private set; } = null!;
    /// <summary>Gets owned premultiplied particle radiance after resolve.</summary>
    internal DynamicTexture2D VisibleRadiance { get; private set; } = null!;
    /// <summary>Retains the engine-backed SSAO restoration intentions for this primary publication.</summary>
    internal RHIRenderPassDescription? SsaoPass { get; private set; }
    /// <summary>Reports whether the original particle SSAO outputs have isolated storage.</summary>
    internal bool HasSsao => SsaoPass is not null;
    /// <summary>Gets straight-alpha-blended radiance, with accumulated coverage in alpha.</summary>
    internal GpuTextureRef ParticleColor { get; }
    /// <summary>Gets the depth before the original particle callback.</summary>
    internal GpuTextureRef BeforeDepth { get; }
    /// <summary>Gets the engine depth immediately after the original particle callback.</summary>
    internal GpuTextureRef AfterDepth { get; }
    /// <summary>Gets the depth image used by the engine for particle visibility and later opaque draws.</summary>
    internal GpuTextureRef VisibilityDepth => visibilityDepthInput;
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
            var depth = GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferSlot.Depth);
            var glow = GpuFramebufferAttachmentDiscovery.Read(primary, FramebufferSlot.Color1);
            // Exact copies let the resolve identify surviving particle depth without
            // guessing a tolerance that could include a later foreground surface.
            if (depth.InternalFormat is not (TextureFormat.DepthComponent32 or TextureFormat.DepthComponent32f) || depth.Samples != 0
                || depth.TextureId == 0 || glow.Width != depth.Width || glow.Height != depth.Height)
                throw new InvalidOperationException("Particle capture requires matching single-sample primary 32-bit depth and glow images.");
            var color = Allocate(TextureFormat.Rgba16f, "Radiance");
            var before = Allocate(depth.InternalFormat, "BeforeDepth");
            var after = Allocate(depth.InternalFormat, "AfterDepth");
            // Owned allocations already have canonical texture recipients. Retain those
            // directly; only the engine visibility image needs external admission.
            ParticleColor = new((GpuResource<GpuTextureMetadata>)color.Resource!);
            BeforeDepth = new((GpuResource<GpuTextureMetadata>)before.Resource!);
            AfterDepth = new((GpuResource<GpuTextureMetadata>)after.Resource!);
            BorrowedTexture.GetOrReplace(ref visibilityDepthInput, depth.TextureId);
            var receiver = Allocate(TextureFormat.R32f, "ReceiverDepth");
            var visible = Allocate(TextureFormat.Rgba16f, "VisibleRadiance");
            if (captureSsao)
            {
                var normal = Allocate(TextureFormat.Rgba16f, "SsaoNormal");
                var position = Allocate(TextureFormat.Rgba16f, "SsaoPosition");
                DrawTarget = DynamicGpuFramebuffer.Create([color, glow, normal, position], depth, "SceneColor.Particles.Draw");
                // Deferred lighting keeps the original position attachment until
                // composition finishes. Only then are surviving particle values restored.
                BorrowedTexture.GetOrReplace(ref normalOutput,GpuFramebufferAttachmentDiscovery.Read(primary,FramebufferSlot.Color2).TextureId);
                BorrowedTexture.GetOrReplace(ref positionOutput,GpuFramebufferAttachmentDiscovery.Read(primary,FramebufferSlot.Color3).TextureId);
                SsaoPass=new([new(Image:RHIAttachment.FromTexture(normalOutput)),new(Image:RHIAttachment.FromTexture(positionOutput))],label:"SceneColor.Particles.SsaoRestore");
            }
            else DrawTarget = DynamicGpuFramebuffer.Create([color, glow], depth, "SceneColor.Particles.Draw");
            MaterialDepth=(DynamicTexture2D)receiver.Resource!;
            VisibleRadiance=(DynamicTexture2D)visible.Resource!;
            ResolvePass=new([new(Image:RHIAttachment.FromTexture(MaterialDepth)),new(Image:RHIAttachment.FromTexture(VisibleRadiance))],label:"SceneColor.Particles.Resolve");
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
        CopyDepth(BeforeDepth);
        // Clear storage directly so indexed write masks and borrowed glow/depth
        // remain untouched. Unsupported setup cannot publish an HDR capture.
        DrawTarget[0].ClearToZero();
        if (HasSsao) { DrawTarget[2].ClearToZero(); DrawTarget[3].ClearToZero(); }
        captureStarted = true;
    }

    /// <summary>Records visibility after the unchanged particle callback completes successfully.</summary>
    internal void EndCapture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrent || !captureStarted)
            throw new InvalidOperationException("Particle capture has no matching current-frame start.");
        captureStarted = false;
        CopyDepth(AfterDepth);
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
        visibilityDepthInput?.Dispose();
        visibilityDepthInput = null;
        Invalidate();
        DrawTarget?.Dispose();
        normalOutput?.Dispose();positionOutput?.Dispose();
        foreach (var image in ownedImages) image.Dispose();
        ownedImages.Clear();
    }
    #endregion

    #region Private
    /// <summary>Copies the whole depth image without disturbing indexed G-buffer blend or write masks.</summary>
    private void CopyDepth(GpuTextureRef destination)
    {
        var area=new RenderArea(0,0,primary.Width,primary.Height);
        RHICommandContext.Current.BlitImages(new(RHIAttachment.FromTexture(VisibilityDepth),area,
            RHIAttachment.FromTexture(destination),area,ImageAspects.Depth));
    }

    /// <summary>Tracks owned allocations immediately so partial setup failures cannot leak storage.</summary>
    private GpuFramebufferAttachment Allocate(TextureFormat format, string name)
    {
        if (format is TextureFormat.DepthComponent32 or TextureFormat.DepthComponent32f)
        {
            var depth = new DepthTexture(primary.Width, primary.Height, format,
                debugName: "SceneColor.Particles." + name);
            ownedImages.Add(depth);
            var depthImage = GpuFramebufferAttachment.FromTexture(depth);
            ownedImages.Add(depthImage);
            return depthImage;
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
