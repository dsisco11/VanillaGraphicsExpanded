namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits work only on its originating execution context without exposing a native implementation.</summary>
internal interface IRHIContextLifetime
{
    /// <summary>Reports whether the original context and calling thread are current.</summary>
    bool IsCurrentContext { get; }
    /// <summary>Reports terminal retirement without entering a replacement context.</summary>
    bool HasRetiredOrigin { get; }
    /// <summary>Rejects work outside the original context and thread.</summary>
    void RequireContext();
}
