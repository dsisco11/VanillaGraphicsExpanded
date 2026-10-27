namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies the unsigned scalar stored in an index stream without exposing native tokens.</summary>
public enum IndexFormat
{
    /// <summary>One unsigned byte per index.</summary>
    UnsignedByte,
    /// <summary>One unsigned sixteen-bit value per index.</summary>
    UnsignedShort,
    /// <summary>One unsigned thirty-two-bit value per index.</summary>
    UnsignedInt
}
