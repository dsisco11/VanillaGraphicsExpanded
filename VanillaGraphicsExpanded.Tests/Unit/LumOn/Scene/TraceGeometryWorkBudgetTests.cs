using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks geometry work-budget policy and storage accounting without constructing a worker.</summary>
public sealed class TraceGeometryWorkBudgetTests
{
    #region Public API
    #region Budget policy
    /// <summary>Persisted coverage requests round upward to supported bounded tiers.</summary>
    [Theory]
    [InlineData(-1,16)] [InlineData(17,32)] [InlineData(65,128)]
    [InlineData(129,192)] [InlineData(192,192)] [InlineData(193,256)] [InlineData(999,256)]
    public void CoverageSanitizesToSupportedTier(int requested, int expected)
    {
        // Arrange
        var config = new VgeConfig();
        config.LumOn.LumonScene.TraceScene.ClipmapResolution = requested;
        // Act
        config.Sanitize();
        // Assert
        Assert.Equal(expected, config.LumOn.LumonScene.TraceScene.ClipmapResolution);
    }
    /// <summary>Defaults increase useful coverage while preserving previous per-frame throughput limits.</summary>
    [Fact]
    public void DefaultsRetainBoundedWork()
    {
        // Arrange
        var config = new VgeConfig();
        Assert.Equal(192, config.LumOn.LumonScene.TraceScene.ClipmapResolution);
        // Act and Assert
        Assert.Equal(new TraceGeometryWorkBudget(2,16,8L << 20), TraceGeometryWorkBudget.From(config.LumOn.LumonScene.TraceScene));
    }

    /// <summary>Both direct use and persisted sanitization keep work within explicit safety bounds.</summary>
    [Theory]
    [InlineData(-1,-1,-1,0,0,0)] [InlineData(99,99,99999999,8,32,8388608)]
    [InlineData(1,1,1,1,1,65536)] [InlineData(0,0,0,0,0,0)]
    public void WorkLimitsAreClamped(int source,int cells,long bytes,int expectedSource,int expectedCells,long expectedBytes)
    {
        // Arrange
        var config = new VgeConfig();
        var trace = config.LumOn.LumonScene.TraceScene;
        trace.SourceChunksPerFrame=source; trace.CellUploadsPerFrame=cells; trace.UploadBytesPerFrame=bytes;
        var expected = new TraceGeometryWorkBudget(expectedSource,expectedCells,expectedBytes);
        // Act and Assert
        Assert.Equal(expected,TraceGeometryWorkBudget.From(trace));
        config.Sanitize();
        Assert.Equal(expected,TraceGeometryWorkBudget.From(trace));
        Assert.Equal(expectedSource,trace.SourceChunksPerFrame);
        Assert.Equal(expectedCells,trace.CellUploadsPerFrame);
        Assert.Equal(expectedBytes,trace.UploadBytesPerFrame);
    }
    /// <summary>Texture accounting includes all voxel companions, readiness and fixed shared lookup payloads.</summary>
    [Theory]
    [InlineData(208)] [InlineData(272)]
    public void TextureAccountingBoundsExpandedStorage(int physical)
    {
        // Arrange
        long cells=physical >> 4;
        long expected=(long)physical*physical*physical*12+cells*cells*cells+2097152+644;
        // Act and Assert
        Assert.Equal(expected,TraceGeometryCoverage.TextureBytes(physical));
        Assert.Throws<ArgumentOutOfRangeException>(()=>TraceGeometryCoverage.TextureBytes(288));
    }

    #endregion
    #endregion
}
