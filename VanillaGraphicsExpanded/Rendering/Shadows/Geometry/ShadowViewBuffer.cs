using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns immutable per-batch projection records exposed through the existing texture-buffer API.</summary>
internal sealed class ShadowViewBuffer : IDisposable
{
    private readonly GpuShaderStorageBuffer records;
    private readonly GpuTexture rasterTarget;
    private readonly (int Width, int Height)[] regions;
    private readonly long allocatedBytes;
    private readonly ImmutableArray<ShadowDrawView> views;
    private readonly ShadowPagePool pages;
    private readonly long pageBatch;
    private GpuFence? completion;
    private bool disposed, sealedBatch;
    internal GpuBufferTexture Texture { get; }
    internal int Count { get; }
    internal long AllocatedBytes => disposed ? 0 : allocatedBytes;

    #region Public API
    /// <summary>Checks aggregate raster/record budgets and baseline stage limits before publishing frozen records.</summary>
    internal ShadowViewBuffer(IReadOnlyList<ShadowDrawView> views, ShadowPagePool pool, ShadowDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(views); ArgumentNullException.ThrowIfNull(pool);
        var policy = settings.Freeze(); var caps = GpuSupport.Graphics;
        if (!pool.CanSubmit || views.Count < 1 || views.Count > policy.ViewCapacity ||
            views.Count * 256L > policy.MemoryMiB * 1048576L || views.Count * 16L > caps.MaxTextureBufferSize ||
            caps.MaxVertexTextureImageUnits < 1 || caps.MaxTessEvaluationTextureImageUnits < 1)
            throw new InvalidOperationException("View records exceed the current batch, budget or texture-buffer limits.");
        long pixels = 0;
        int width = 0, height = 0;
        regions = new (int, int)[views.Count];
        var words = new uint[checked(views.Count * 64)];
        for (int i = 0; i < views.Count; i++)
        {
            var view = views[i];
            regions[i] = (view.Region.Width, view.Region.Height);
            width = Math.Max(width, view.Region.Width); height = Math.Max(height, view.Region.Height);
            pixels = checked(pixels + view.Region.Width * (long)view.Region.Height);
            if (view.Region.Width > caps.MaxViewportWidth || view.Region.Height > caps.MaxViewportHeight ||
                view.Region.Width > caps.MaxTextureSize || view.Region.Height > caps.MaxTextureSize ||
                pixels > policy.RasterPixels || view.Projection.PageSize != pool.PageSize)
                throw new InvalidOperationException("Virtual raster region requires an explicitly budgeted split.");
            view.Pack(words.AsSpan(i * 64, 64), pool.Capacity, pool.ProbeLimit, pool.ArrayLayers, pool.Batch);
        }
        Count = views.Count;
        this.views = ImmutableArray.CreateRange(views); pages = pool; pageBatch = pool.Batch;
        // A single R8 attachment provides a valid raster pass while the shadow depth
        // lives exclusively in integer images. It is charged with the immutable records.
        allocatedBytes = checked(Count * 256L + width * (long)height);
        if (allocatedBytes > policy.MemoryMiB * 1048576L)
            throw new InvalidOperationException("Raster attachment and view records exceed the drawing memory budget.");
        var rhi = RHICommandContext.Current;
        records = rhi.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage,
            Mode = ResourceStorageMode.Mutable, Capacity = checked(words.Length * sizeof(uint)),
            Usage = BufferUsage.StaticDraw, Label = "Shadows.Views" }).RequireResource();
        try
        {
            records.UploadSubData<uint>(words, 0);
            Texture = rhi.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = records,
                Format = TextureFormat.Rgba32ui, WholeBuffer = true, Label = "Shadows.ViewTexture" }).RequireResource();
            rasterTarget = rhi.CreateTexture(new RHITextureDescription { Dimension = TextureDimension.Two,
                Format = TextureFormat.R8, Width = width, Height = height, Depth = 1, MipLevels = 1,
                Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
                BaseLevel = 0, MaxLevel = 0, Label = "Shadows.VirtualRaster" }).RequireResource();
        }
        catch { Texture?.Dispose(); records.Dispose(); throw; }
    }

    /// <summary>Requires the exact immutable view and page interval used when these GPU records were packed.</summary>
    internal bool Matches(int index, ShadowDrawView view, ShadowPagePool pool) => !disposed && !sealedBatch &&
        ReferenceEquals(pages, pool) && pool.CanSubmit && pool.Batch == pageBatch &&
        (uint)index < (uint)views.Length && ReferenceEquals(views[index], view);

    /// <summary>Describes an origin-zero raster area for one view without exposing or binding native framebuffer state.</summary>
    internal RHIRenderPassDescription DescribePass(int view)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch || (uint)view >= (uint)regions.Length) throw new ArgumentOutOfRangeException(nameof(view));
        var region = regions[view];
        return new([new(Image: RHIAttachment.FromTexture(rasterTarget), Load: AttachmentLoad.Discard,
            Store: AttachmentStore.Discard)], area: new(0, 0, region.Width, region.Height), label: "Shadows.VirtualRaster");
    }

    /// <summary>Ends submission and retains charged storage until all preceding GPU users complete.</summary>
    internal void Seal()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (sealedBatch) throw new InvalidOperationException("View records were already sealed.");
        completion = RHICommandContext.Current.InsertFence(); sealedBatch = true;
    }

    /// <summary>Retires a sealed buffer only after nonblocking completion, preserving in-flight byte accounting.</summary>
    internal bool Poll()
    {
        if (disposed) return true;
        if (!sealedBatch || completion is null || !completion.TryConsumeIfSignaled()) return false;
        completion = null; Dispose(); return true;
    }

    /// <summary>Ends terminal ownership through existing RHI resource retirement; normal draining uses Seal/Poll.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; completion?.Dispose(); completion = null; rasterTarget.Dispose(); Texture.Dispose(); records.Dispose();
    }
    #endregion
}
