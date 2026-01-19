namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct MappedFlushRange(int OffsetBytes, int SizeBytes)
{
    public static MappedFlushRange None => default;

    public bool IsEmpty => SizeBytes <= 0;
}
