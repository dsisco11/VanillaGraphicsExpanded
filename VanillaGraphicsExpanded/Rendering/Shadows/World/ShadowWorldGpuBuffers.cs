using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.World;

/// <summary>Uploads bounded immutable world descriptions through the existing RHI ownership boundary.</summary>
internal sealed class ShadowWorldGpuBuffers : IDisposable
{
    private GpuShaderStorageBuffer? table, ranges;
    private long generation, owner;
    private long tableBytes,rangeBytes;
    private readonly Queue<(GpuShaderStorageBuffer Table,GpuShaderStorageBuffer Ranges,GpuFence Fence,long Bytes)> retired=new();

    #region Public API
    /// <summary>Gets the coordinate table only after a successful complete upload.</summary>
    internal GpuShaderStorageBuffer Table => table ?? throw new InvalidOperationException("No world upload.");
    /// <summary>Gets native range descriptions; zero range counts do not address this buffer.</summary>
    internal GpuShaderStorageBuffer Ranges => ranges ?? throw new InvalidOperationException("No world upload.");
    /// <summary>Reports actual owned payload allocation, including a minimum empty-range sentinel.</summary>
    internal long AllocatedBytes { get; private set; }

    /// <summary>Reports new allocation bytes before a growing publication; old buffers stay charged through retirement.</summary>
    internal long AdditionalBytesFor(ShadowWorldPublication publication)
    {
        Poll();
        long a=publication.Table.Length*64L,b=Math.Max(32,publication.Ranges.Length*32L);
        return a>tableBytes || b>rangeBytes?checked(a+b):0;
    }

    /// <summary>Checks the exact upload owner and generation before another GPU producer borrows the lookup.</summary>
    internal bool Matches(ShadowWorldPublication publication) => publication.Complete && generation == publication.Generation
        && owner == publication.Owner && table is { IsValid: true } && ranges is { IsValid: true };

    /// <summary>Replaces storage only for a changed publication; failed or partial inputs withdraw the certificate.</summary>
    internal bool Upload(ShadowWorldPublication publication)
    {
        Poll();
        if (!publication.Complete) { generation = 0; return false; }
        if (generation == publication.Generation && owner == publication.Owner
            && table is { IsValid: true } && ranges is { IsValid: true }) return true;
        generation = 0;
        try
        {
            long a=publication.Table.Length*64L,b=Math.Max(32,publication.Ranges.Length*32L);
            if(a>tableBytes || b>rangeBytes)
            {
                if(retired.Count>=8)throw new InvalidOperationException("World upload retirement capacity is exhausted.");
                var nextTable=Create("Shadows.World.Lookup");GpuShaderStorageBuffer? nextRanges=null;
                try
                {
                    nextRanges=Create("Shadows.World.Ranges");nextTable.UploadData(publication.Table.AsSpan());
                    if(publication.Ranges.IsEmpty)nextRanges.UploadData<uint>(new uint[8]);else nextRanges.UploadData(publication.Ranges.AsSpan());
                }
                catch{nextTable.Dispose();nextRanges?.Dispose();throw;}
                if(table is not null && ranges is not null)retired.Enqueue((table,ranges,RHICommandContext.Current.InsertFence(),checked(tableBytes+rangeBytes)));
                table=nextTable;ranges=nextRanges;tableBytes=a;rangeBytes=b;
                AllocatedBytes=checked(AllocatedBytes+a+b);
            }
            else
            {
                // Reuse fixed storage without orphaning an untracked generation.
                // RHI orders these updates after previously queued consumers.
                table!.UploadSubData(publication.Table.AsSpan(),0);
                if(publication.Ranges.IsEmpty)ranges!.UploadSubData<uint>(new uint[8],0);
                else ranges!.UploadSubData(publication.Ranges.AsSpan(),0);
            }
            generation = publication.Generation;
            owner = publication.Owner;
            return true;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Withdraws the upload identity before releasing only owned metadata buffers.</summary>
    public void Dispose()
    {
        generation = 0;
        table?.Dispose(); ranges?.Dispose(); table = ranges = null;
        while(retired.TryDequeue(out var old)){old.Fence.Dispose();old.Table.Dispose();old.Ranges.Dispose();}
        tableBytes=rangeBytes=0;
        AllocatedBytes = 0;
    }
    #endregion

    #region Private
    /// <summary>Retires growing-upload generations only when their earlier GPU users have completed.</summary>
    private void Poll()
    {
        while(retired.TryPeek(out var old) && old.Fence.TryConsumeIfSignaled())
        {retired.Dequeue();old.Table.Dispose();old.Ranges.Dispose();AllocatedBytes-=old.Bytes;}
    }
    /// <summary>Requests manager-owned mutable storage with no optional graphics feature.</summary>
    private static GpuShaderStorageBuffer Create(string label) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable,
            Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = label }).RequireResource();
    #endregion
}
