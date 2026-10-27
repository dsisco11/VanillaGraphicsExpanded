using System;
using VanillaGraphicsExpanded.Rendering.RHI;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL element/index buffer object (EBO / IBO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuEbo : GpuBufferObject
{
    private int indexCount;
    private IndexFormat indexType;

    public int IndexCount => indexCount;
    public IndexFormat IndexType => indexType;

    /// <summary>Declares index-buffer policy without native allocation or publication.</summary>
    internal GpuEbo(BufferUsage usage, string? debugName)
    {
        Role = BufferRole.Index;
        allocationUsage = usage;
        this.debugName = debugName;
    }


    public void UploadIndices(uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedInt;
    }

    public bool TryUploadIndices(uint[] indices)
    {
        if (indices is null)
        {
            return false;
        }

        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedInt;
        return true;
    }

    public void UploadIndices(ReadOnlySpan<uint> indices)
    {
        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedInt;
    }

    public bool TryUploadIndices(ReadOnlySpan<uint> indices)
    {
        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedInt;
        return true;
    }

    public void UploadIndices(ushort[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedShort;
    }

    public bool TryUploadIndices(ushort[] indices)
    {
        if (indices is null)
        {
            return false;
        }

        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedShort;
        return true;
    }

    public void UploadIndices(ReadOnlySpan<ushort> indices)
    {
        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedShort;
    }

    public bool TryUploadIndices(ReadOnlySpan<ushort> indices)
    {
        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedShort;
        return true;
    }

    public void UploadIndices(byte[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedByte;
    }

    public bool TryUploadIndices(byte[] indices)
    {
        if (indices is null)
        {
            return false;
        }

        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedByte;
        return true;
    }

    public void UploadIndices(ReadOnlySpan<byte> indices)
    {
        UploadData(indices);
        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedByte;
    }

    public bool TryUploadIndices(ReadOnlySpan<byte> indices)
    {
        if (!TryUploadData(indices))
        {
            return false;
        }

        indexCount = indices.Length;
        indexType = IndexFormat.UnsignedByte;
        return true;
    }

    protected override void OnDetached(nint id)
    {
        base.OnDetached(id);
        indexCount = 0;
        indexType = default;
    }

    protected override void OnAfterDelete()
    {
        base.OnAfterDelete();
        indexCount = 0;
        indexType = default;
    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={bufferId}, sizeBytes={sizeBytes}, usage={usage}, indexCount={indexCount}, indexType={indexType}, name={debugName}, disposed={IsDisposed})";
    }
}
