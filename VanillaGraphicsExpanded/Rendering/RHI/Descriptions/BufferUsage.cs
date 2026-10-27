namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Expresses update frequency and intended producer/consumer access.</summary>
public enum BufferUsage
{
    /// <summary>No usable storage usage has been published; this is not a legal allocation request.</summary>
    Undefined,
    /// <summary>Expresses infrequent CPU updates for rendering.</summary>
    StaticDraw,
    /// <summary>Expresses infrequent GPU updates for CPU reads.</summary>
    StaticRead,
    /// <summary>Expresses infrequent GPU updates for GPU reads.</summary>
    StaticCopy,
    /// <summary>Expresses repeated CPU updates for rendering.</summary>
    DynamicDraw,
    /// <summary>Expresses repeated GPU updates for CPU reads.</summary>
    DynamicRead,
    /// <summary>Expresses repeated GPU updates for GPU reads.</summary>
    DynamicCopy,
    /// <summary>Expresses transient CPU updates for rendering.</summary>
    StreamDraw,
    /// <summary>Expresses transient GPU updates for CPU reads.</summary>
    StreamRead,
    /// <summary>Expresses transient GPU updates for GPU reads.</summary>
    StreamCopy,
}
