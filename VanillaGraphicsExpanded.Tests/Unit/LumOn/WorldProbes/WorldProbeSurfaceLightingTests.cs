using System;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies default transferred-value handling without tracing dependencies.</summary>
public sealed class WorldProbeSurfaceLightingTests
{
    #region Public API
    /// <summary>A default immutable payload is safely treated as no publishable samples.</summary>
    [Fact]
    public void DefaultTransferredBatchIsEmptyAndUnavailable()
    {
        // Arrange
        LumOnWorldProbeTraceResult source=default;
        int index=0;
        // Act
        var result=WorldProbeSurfaceLighting.Resolve(source,[],ref index);
        // Assert
        Assert.False(result.Success);Assert.True(result.AtlasSamples.IsDefaultOrEmpty);
        Assert.True(result.RetrySamples.IsDefaultOrEmpty);Assert.Equal(0,index);
    }
    #endregion
}
