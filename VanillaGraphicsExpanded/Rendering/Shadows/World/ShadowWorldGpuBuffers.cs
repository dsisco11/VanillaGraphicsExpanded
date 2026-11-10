using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Shadows.World;

/// <summary>Uploads bounded immutable world descriptions through the existing RHI ownership boundary.</summary>
internal sealed class ShadowWorldGpuBuffers : IDisposable
{
    private GpuShaderStorageBuffer? table, ranges;
    private long generation, owner;

    #region Public API
    /// <summary>Gets the coordinate table only after a successful complete upload.</summary>
    internal GpuShaderStorageBuffer Table => table ?? throw new InvalidOperationException("No world upload.");
    /// <summary>Gets native range descriptions; zero range counts do not address this buffer.</summary>
    internal GpuShaderStorageBuffer Ranges => ranges ?? throw new InvalidOperationException("No world upload.");
    /// <summary>Reports actual owned payload allocation, including a minimum empty-range sentinel.</summary>
    internal long AllocatedBytes { get; private set; }

    /// <summary>Replaces storage only for a changed publication; failed or partial inputs withdraw the certificate.</summary>
    internal bool Upload(ShadowWorldPublication publication)
    {
        if (!publication.Complete) { generation = 0; return false; }
        if (generation == publication.Generation && owner == publication.Owner
            && table is { IsValid: true } && ranges is { IsValid: true }) return true;
        generation = 0;
        try
        {
            table ??= Create("Shadows.World.Lookup");
            ranges ??= Create("Shadows.World.Ranges");
            // BufferData orphaning preserves already queued GPU users through the RHI/driver lifetime.
            table.UploadData(publication.Table.AsSpan());
            if (publication.Ranges.IsEmpty) ranges.UploadData<uint>(new uint[8]);
            else ranges.UploadData(publication.Ranges.AsSpan());
            AllocatedBytes = publication.PackedBytes + (publication.Ranges.IsEmpty ? 32 : 0);
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
        AllocatedBytes = 0;
    }
    #endregion

    #region Private
    /// <summary>Requests manager-owned mutable storage with no optional graphics feature.</summary>
    private static GpuShaderStorageBuffer Create(string label) => RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable,
            Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = label }).RequireResource();
    #endregion
}
