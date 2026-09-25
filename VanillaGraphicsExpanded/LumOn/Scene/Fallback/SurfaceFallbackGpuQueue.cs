using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Owns one fixed-capacity GPU request readback; busy or failed fences never permit buffer reuse.</summary>
internal sealed class SurfaceFallbackGpuQueue : IDisposable
{
    public GpuShaderStorageBuffer Buffer { get; } = GpuShaderStorageBuffer.Create(debugName:"SurfaceLighting.FallbackRequests");
    private GpuFence? fence;
    public bool Pending => fence != null;

    #region Submission and collection
    /// <summary>Allocates a sixteen-byte header and exactly sixteen 64-byte request records.</summary>
    public SurfaceFallbackGpuQueue() => Buffer.EnsureCapacity(16 + (SurfaceFallbackWorker.MaximumTexels << 6),growExponentially:false);

    /// <summary>Clears only the append header before an idle queue is bound to one indirect dispatch.</summary>
    public void Begin(int pages, uint selection)
    {
        if (Pending || pages <= 0) throw new InvalidOperationException("Fallback queue is not idle.");
        Buffer.UploadSubData<uint>(new uint[]{0,SurfaceFallbackWorker.MaximumTexels,(uint)pages,selection},0,16);
    }

    /// <summary>Fences the producer without waiting for shader results.</summary>
    public void Submit()
    {
        if (Pending) throw new InvalidOperationException("Fallback already submitted.");
        fence = GpuFence.Insert(); GL.Flush();
    }

    /// <summary>Maps bounded records only after a successful completion poll; failed readbacks reject the batch.</summary>
    public bool TryRead(out ImmutableArray<SurfaceFallbackRequest> requests)
    {
        requests = ImmutableArray<SurfaceFallbackRequest>.Empty;
        if (fence == null) return false;
        var status = fence.Poll();
        if (status == WaitSyncStatus.TimeoutExpired) return false;
        if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Fallback request fence failed.");
        int count;
        using (var header = Buffer.MapRange<uint>(0,4,MapBufferAccessMask.MapReadBit))
        {
            if (!header.IsMapped) throw new InvalidOperationException("Fallback header readback failed.");
            count = (int)Math.Min(header.Span[0],SurfaceFallbackWorker.MaximumTexels);
        }
        if (count > 0)
        {
            using var read = Buffer.MapRange<SurfaceFallbackRequest>(16,count,MapBufferAccessMask.MapReadBit);
            if (!read.IsMapped) throw new InvalidOperationException("Fallback request readback failed.");
            requests = ImmutableArray.Create(read.Span);
        }
        fence.Dispose(); fence = null;
        return true;
    }
    #endregion

    /// <summary>Retires storage along with its fence instead of overwriting an unknown completion.</summary>
    public void Dispose() { fence?.Dispose(); fence = null; Buffer.Dispose(); }
}
