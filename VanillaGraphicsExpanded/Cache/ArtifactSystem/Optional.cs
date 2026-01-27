using System;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Minimal, allocation-free optional value container.
/// Works for both structs and reference types.
/// </summary>
internal readonly struct Optional<T>
{
    public bool HasValue { get; }

    public T Value { get; }

    public Optional(T value)
    {
        HasValue = true;
        Value = value;
    }

    public T GetValueOrDefault(T defaultValue)
        => HasValue ? Value : defaultValue;

    public override string ToString()
        => HasValue ? Value?.ToString() ?? "(null)" : "(none)";
}
