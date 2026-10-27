using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes a location-sorted vertex layout with structural identity and context-independent shape validation.</summary>
public sealed class RHIVertexLayout : IEquatable<RHIVertexLayout>
{
    #region Public API
    #region Construction and inspection
    /// <summary>Gets immutable attribute and stream packing declarations without native buffer identities.</summary>
    public IReadOnlyList<RHIVertexAttribute> Attributes { get; }
    /// <summary>Copies caller inputs and rejects ambiguous locations, invalid packing or incompatible scalar interpretations.</summary>
    public RHIVertexLayout(IEnumerable<RHIVertexAttribute> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        Attributes = Array.AsReadOnly(attributes.OrderBy(attribute => attribute.Location).ToArray());
        if (Attributes.Select(attribute => attribute.Location).Distinct().Count() != Attributes.Count)
            throw new ArgumentException("Duplicate attribute location.", nameof(attributes));
        foreach (var attribute in Attributes)
        {
            bool integer = attribute.Storage is VertexComponentFormat.SignedByte or VertexComponentFormat.UnsignedByte
                or VertexComponentFormat.SignedShort or VertexComponentFormat.UnsignedShort
                or VertexComponentFormat.SignedInt or VertexComponentFormat.UnsignedInt;
            int size = attribute.Storage switch
            {
                VertexComponentFormat.SignedByte or VertexComponentFormat.UnsignedByte => 1,
                VertexComponentFormat.SignedShort or VertexComponentFormat.UnsignedShort or VertexComponentFormat.Half => 2,
                VertexComponentFormat.SignedInt or VertexComponentFormat.UnsignedInt or VertexComponentFormat.Float => 4,
                VertexComponentFormat.Double => 8,
                _ => throw new ArgumentException("Unsupported attribute storage.", nameof(attributes))
            };
            if (!Enum.IsDefined(attribute.Interpretation) || attribute.Location < 0 || attribute.Binding < 0 || attribute.Offset < 0
                || attribute.Divisor < 0 || attribute.Components is < 1 or > 4 || attribute.Stride < size * attribute.Components
                || attribute.Offset > attribute.Stride - size * attribute.Components
                || (attribute.Interpretation is VertexInterpretation.Integer or VertexInterpretation.Normalized && !integer)
                || (attribute.Interpretation == VertexInterpretation.Double && attribute.Storage != VertexComponentFormat.Double))
                throw new ArgumentException("Invalid vertex attribute layout.", nameof(attributes));
        }
        // Attribute formats belong to locations; stride and instance rate belong to their shared stream.
        if (Attributes.GroupBy(attribute => attribute.Binding).Any(group =>
            group.Select(attribute => (attribute.Stride, attribute.Divisor)).Distinct().Count() != 1))
            throw new ArgumentException("Shared vertex bindings disagree on stride or divisor.", nameof(attributes));
    }
    #endregion

    #region Structural identity
    /// <summary>Compares all immutable attributes and packing declarations in canonical location order.</summary>
    public bool Equals(RHIVertexLayout? other) => other is not null && Attributes.SequenceEqual(other.Attributes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RHIVertexLayout other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var attribute in Attributes) hash.Add(attribute);
        return hash.ToHashCode();
    }
    /// <summary>Compares complete layout intent, including null references.</summary>
    public static bool operator ==(RHIVertexLayout? left, RHIVertexLayout? right) =>
        ReferenceEquals(left, right) || left is not null && left.Equals(right);
    /// <summary>Reports unequal layout declarations.</summary>
    public static bool operator !=(RHIVertexLayout? left, RHIVertexLayout? right) => !(left == right);
    #endregion
    #endregion
}
