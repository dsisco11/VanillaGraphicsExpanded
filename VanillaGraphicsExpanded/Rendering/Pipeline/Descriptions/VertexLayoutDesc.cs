using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Copied, location-sorted vertex layout; buffer instances remain draw bindings.</summary>
internal sealed record VertexLayoutDesc
{
    public PipelineValues<VertexAttributeDesc> Attributes { get; }

    #region Public API
    /// <summary>Rejects ambiguous locations, invalid packing and incompatible scalar interpretations.</summary>
    public VertexLayoutDesc(IEnumerable<VertexAttributeDesc> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        Attributes = new(attributes.OrderBy(a => a.Location));
        if (Attributes.Select(a => a.Location).Distinct().Count() != Attributes.Count)
            throw new ArgumentException("Duplicate attribute location.", nameof(attributes));
        foreach (var a in Attributes)
        {
            bool integer = a.Storage is VertexAttribPointerType.Byte or VertexAttribPointerType.UnsignedByte
                or VertexAttribPointerType.Short or VertexAttribPointerType.UnsignedShort
                or VertexAttribPointerType.Int or VertexAttribPointerType.UnsignedInt;
            int size = a.Storage switch
            {
                VertexAttribPointerType.Byte or VertexAttribPointerType.UnsignedByte => 1,
                VertexAttribPointerType.Short or VertexAttribPointerType.UnsignedShort or VertexAttribPointerType.HalfFloat => 2,
                VertexAttribPointerType.Int or VertexAttribPointerType.UnsignedInt or VertexAttribPointerType.Float => 4,
                VertexAttribPointerType.Double => 8,
                _ => throw new ArgumentException("Unsupported attribute storage.", nameof(attributes))
            };
            if (!Enum.IsDefined(a.Interpretation) || a.Location < 0 || a.Binding < 0 || a.Offset < 0 || a.Divisor < 0
                || a.Components is < 1 or > 4 || a.Stride < size * a.Components
                || a.Offset > a.Stride - size * a.Components
                || (a.Interpretation is VertexInterpretation.Integer or VertexInterpretation.Normalized && !integer)
                || (a.Interpretation == VertexInterpretation.Double && a.Storage != VertexAttribPointerType.Double))
                throw new ArgumentException("Invalid vertex attribute layout.", nameof(attributes));
        }
        // Attributes sharing a binding must agree on the binding's stride and instance rate.
        if (Attributes.GroupBy(a => a.Binding).Any(g => g.Select(a => (a.Stride, a.Divisor)).Distinct().Count() != 1))
            throw new ArgumentException("Shared vertex bindings disagree on stride or divisor.", nameof(attributes));
    }
    #endregion
}
