namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Identifies an immutable uploaded range and its last submitted use.</summary>
internal sealed class UniformStorageVersion
{
    internal readonly PersistentUniformStorage Owner;
    internal readonly UniformStoragePage Page;
    internal readonly int Slot;
    internal readonly long Generation;
    internal readonly int Size;
    internal long LastUse;
    internal bool Released;
    internal int Offset => Slot * Page.SlotSize;

    #region Public API
    /// <summary>Captures allocator and slot identity before publication.</summary>
    internal UniformStorageVersion(PersistentUniformStorage owner, UniformStoragePage page, int slot, int size)
    {
        Owner = owner; Page = page; Slot = slot; Size = size;
        Generation = ++page.Generations[slot];
    }
    #endregion
}
