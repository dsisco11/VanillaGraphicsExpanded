using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains externally packed uniform bytes for a single generated submission.</summary>
internal sealed class PackedUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Creates fixed-size zero-initialized uniform storage.</summary>
    public PackedUniformBuffer(int size) : base(size) { }

    /// <summary>Copies a complete packed block without uploading or losing prior dirty state.</summary>
    public void SetBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != SizeBytes) throw new ArgumentException("Packed block size must remain fixed.", nameof(bytes));
        var destination = DataWritable;
        if (bytes.SequenceEqual(destination)) return;
        bytes.CopyTo(destination);
        MarkDirty();
    }
    #endregion
}
