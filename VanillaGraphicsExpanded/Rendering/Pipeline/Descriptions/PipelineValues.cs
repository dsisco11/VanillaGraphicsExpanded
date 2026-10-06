using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Owns a copied sequence of immutable pipeline values with structural equality.</summary>
internal sealed class PipelineValues<T> : IReadOnlyList<T>, IEquatable<PipelineValues<T>> where T : notnull
{
    private readonly T[] values;
    public int Count => values.Length;
    public T this[int index] => values[index];

    #region Public API
    #region Construction
    /// <summary>Copies input storage so later caller edits cannot change pipeline identity.</summary>
    public PipelineValues(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        values = source.ToArray();
        if (values.Any(value => value is null)) throw new ArgumentException("Pipeline values cannot contain null.", nameof(source));
    }
    #endregion

    #region Identity and enumeration
    /// <summary>Compares ordered elements, never array identities or hashes alone.</summary>
    public bool Equals(PipelineValues<T>? other) => other is not null && values.SequenceEqual(other.values);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is PipelineValues<T> other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (T value in values) hash.Add(value);
        return hash.ToHashCode();
    }
    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)values).GetEnumerator();
    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion
    #endregion
}
