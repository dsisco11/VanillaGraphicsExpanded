using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks context-free geometry descriptor shape without creating native objects or fabricating identities.</summary>
[Trait("Category", "Unit")]
public sealed class RHIGeometryInputTests
{
    #region Public API
    /// <summary>Procedural packets keep their explicit vertex bound and reject negative ranges at construction.</summary>
    [Fact]
    public void ProceduralBoundsAreExplicit()
    {
        var layout = new RHIVertexLayout([]);
        var packet = new RHIGeometryInputs(layout, [], proceduralVertexCount: 3);
        Assert.Same(layout, packet.Layout);
        Assert.Equal(3, packet.ProceduralVertexCount);
        Assert.Empty(packet.Streams);
        Assert.Null(packet.Indices);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RHIGeometryInputs(layout, [], proceduralVertexCount: -1));
    }

    /// <summary>Every layout binding requires exactly one nonempty retained buffer interval with matching packing.</summary>
    [Fact]
    public void MissingOrUnownedStreamsRejectBeforeContextAccess()
    {
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        Assert.Throws<ArgumentException>(() => new RHIGeometryInputs(layout, []));
        Assert.Throws<ArgumentException>(() => new RHIGeometryInputs(layout, [new(0, new(default, 0, 12), 12)]));
        Assert.Throws<ArgumentException>(() => new RHIGeometryInputs(new([]), [], new(new(default, 0, 12))));
    }
    #endregion
}
