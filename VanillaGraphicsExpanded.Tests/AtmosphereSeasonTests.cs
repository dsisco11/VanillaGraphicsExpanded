using System.Diagnostics;
using System.Numerics;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Validates calendar-only regional snow assumptions without observing terrain.</summary>
public sealed class AtmosphereSeasonTests(ITestOutputHelper output)
{
    #region Regional model
    /// <summary>Warm regions remain bare, cold regions brighten, and spring snow persists longer than autumn snow.</summary>
    [Fact]
    public void TemperatureBoundsAndSeasonalLag()
    {
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(30, 28, 32));
        Assert.Equal((1f, .8f), AtmosphereSeasonModel.Estimate(-30, -30, -30));
        Assert.True(AtmosphereSeasonModel.Estimate(0, -4, 4).SnowCoverage > AtmosphereSeasonModel.Estimate(0, 4, -4).SnowCoverage);
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(float.NaN, 0, 0));
        Assert.Equal((0f, .1f), AtmosphereSeasonModel.Estimate(0, float.PositiveInfinity, 0));
        Assert.Equal(5, AtmosphereSeasonModel.AlbedoBucket(float.NaN));
        Assert.Equal(5, AtmosphereSeasonModel.AlbedoBucket(.101f));
    }

    /// <summary>Controlled hemisphere temperatures exercise seasonal sampling, year wrap and coordinate bucketing.</summary>
    [Fact]
    public void CalendarSamplingAndCachingNeverScanTerrain()
    {
        var calendar = new Mock<IClientGameCalendar>(MockBehavior.Strict);
        double day = 0;
        float? seasonOverride = null;
        calendar.SetupGet(c => c.DaysPerYear).Returns(96);
        calendar.SetupGet(c => c.TotalDays).Returns(() => day);
        calendar.SetupGet(c => c.SeasonOverride).Returns(() => seasonOverride);
        calendar.SetupGet(c => c.OnGetLatitude).Returns(new GetLatitudeDelegate(z => z < 0 ? -.5 : .5));
        var accessor = new Mock<IBlockAccessor>(MockBehavior.Strict);
        int calls = 0;
        bool available = true;
        accessor.Setup(a => a.GetClimateAt(It.IsAny<BlockPos>(), EnumGetClimateMode.WorldGenValues, It.IsAny<double>()))
            .Returns((BlockPos p, EnumGetClimateMode m, double date) => { calls++; return available ? new ClimateCondition() : null!; });
        accessor.Setup(a => a.GetClimateAt(It.IsAny<BlockPos>(), It.IsAny<ClimateCondition>(), EnumGetClimateMode.ForSuppliedDate_TemperatureOnly, It.IsAny<double>()))
            .Returns((BlockPos p, ClimateCondition c, EnumGetClimateMode m, double date) =>
            {
                calls++;
                double phase = seasonOverride ?? (float)(date / 96);
                return new ClimateCondition { Temperature = (float)((p.Z < 0 ? 1 : -1) * 15 * Math.Cos(2 * Math.PI * phase) + 4 * Math.Sin(2 * Math.PI * date)) };
            });
        var world = new Mock<IClientWorldAccessor>(MockBehavior.Strict);
        world.SetupGet(w => w.Calendar).Returns(calendar.Object);
        world.SetupGet(w => w.SeaLevel).Returns(110);
        long elapsed = 0;
        world.SetupGet(w => w.ElapsedMilliseconds).Returns(() => elapsed);
        world.SetupGet(w => w.BlockAccessor).Returns(accessor.Object);
        var api = new Mock<ICoreClientAPI>(MockBehavior.Strict);
        api.SetupGet(a => a.World).Returns(world.Object);
        var input = new AtmosphereSeasonInputs();
        var north = input.Capture(api.Object, new BlockPos(1, 120, 1));
        Assert.InRange(north.MeanTemperature, -16, -14);
        Assert.Equal(37, calls);
        Assert.Equal(north, input.Capture(api.Object, new BlockPos(30, 500, 30)));
        Assert.Equal(37, calls);
        var south = input.Capture(api.Object, new BlockPos(-1, 120, -1));
        Assert.Equal(-.5, south.Latitude);
        Assert.InRange(south.MeanTemperature, 14, 16);
        Assert.True(north.SnowCoverage > south.SnowCoverage);
        day = 48;
        Assert.InRange(input.Capture(api.Object, new BlockPos(1, 120, 1)).MeanTemperature, 14, 16);
        day = 24;
        var warming = input.Capture(api.Object, new BlockPos(1, 120, 1));
        day = 72;
        var cooling = input.Capture(api.Object, new BlockPos(1, 120, 1));
        Assert.True(cooling.TemperatureTrend < 0);
        Assert.True(warming.TemperatureTrend > 0);
        day = 96;
        Assert.InRange(MathF.Abs(north.MeanTemperature - input.Capture(api.Object, new BlockPos(1, 120, 1)).MeanTemperature), 0, .0001f);
        int overrideStart = calls;
        seasonOverride = 0f;
        Assert.InRange(input.Capture(api.Object, new BlockPos(1, 120, 1)).MeanTemperature, -16, -14);
        Assert.Equal(0f, input.Capture(api.Object, new BlockPos(1, 120, 1)).TemperatureTrend);
        Assert.Equal(13, calls - overrideStart);
        available = false; day++;
        Assert.Equal(.1f, input.Capture(api.Object, new BlockPos(1, 120, 1)).GroundAlbedo);
        int missingCalls = calls;
        available = true;
        Assert.Equal(.1f, input.Capture(api.Object, new BlockPos(1, 120, 1)).GroundAlbedo);
        Assert.Equal(missingCalls, calls);
        elapsed = 1000;
        Assert.True(input.Capture(api.Object, new BlockPos(1, 120, 1)).GroundAlbedo > .1f);
        int before = calls;
        Assert.Equal(.1f, input.Capture(api.Object, new BlockPos(1, 120, 1, 1)).GroundAlbedo);
        Assert.Equal(before, calls);
        // Strict mocks reject block reads, height maps and every unapproved terrain API.
        available = true;
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++) input.Capture(api.Object, new BlockPos(1, 120, 1));
        output.WriteLine($"1000 cached snapshot calls: {clock.Elapsed.TotalMilliseconds:F3} ms (mock API; not engine cost).");
        clock.Restart();
        for (int i = 0; i < 100; i++) { day++; input.Capture(api.Object, new BlockPos(1, 120, 1)); }
        output.WriteLine($"100 uncached snapshot calls: {clock.Elapsed.TotalMilliseconds:F3} ms (mock API; not engine cost).");
    }
    #endregion

    #region Transport admission
    /// <summary>The lookup integrates changed reflectance while worker admission reuses its quantized bucket.</summary>
    [Fact]
    public void GroundAlbedoInvalidatesCpuTransport()
    {
        var lookup = new AtmosphereLookup();
        lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 8);
        var bare = lookup.Current!;
        Assert.False(lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 8, groundAlbedo: .101f));
        Assert.True(lookup.Update(Vector3.UnitY, 0, 0, width: 16, height: 8, groundAlbedo: .8f));
        var snowy = lookup.Current!;
        Assert.True(snowy.Environment.Length() > bare.Environment.Length());
        Assert.NotSame(bare, snowy);

        var producer = new ControlledProducer();
        using var worker = new AtmosphereComputation(producer.Start);
        Assert.Null(worker.Update(Vector3.UnitY, 0, 0, 16, 8, groundAlbedo: .1f));
        var bareRequest = Assert.Single(producer.Requests);
        Assert.Equal(Vector3.UnitY, bareRequest.Request.Sun);
        Assert.Equal((0f, 0f, 16, 8, 0, .1f),
            (bareRequest.Request.Altitude, bareRequest.Request.Clouds, bareRequest.Request.Width,
                bareRequest.Request.Height, bareRequest.Request.Quality, bareRequest.Request.GroundAlbedo));
        Assert.NotNull(worker.Pending);

        producer.Complete(0, bare);
        Assert.Same(bare, worker.Update(Vector3.UnitY, 0, 0, 16, 8, groundAlbedo: .8f));
        Assert.Equal(2, producer.Requests.Count);
        var snowyRequest = producer.Requests[1];
        Assert.Equal(Vector3.UnitY, snowyRequest.Request.Sun);
        Assert.Equal((0f, 0f, 16, 8, 0, .8f),
            (snowyRequest.Request.Altitude, snowyRequest.Request.Clouds, snowyRequest.Request.Width,
                snowyRequest.Request.Height, snowyRequest.Request.Quality, snowyRequest.Request.GroundAlbedo));
        Assert.NotNull(worker.Pending);

        producer.Complete(1, snowy);
        var published = worker.Update(Vector3.UnitY, 0, 0, 16, 8, groundAlbedo: .801f);
        Assert.Same(snowy, published);
        Assert.Null(worker.Pending);
        Assert.Equal(2, producer.Requests.Count);
        Assert.Null(worker.Update(Vector3.UnitY, 0, 0, 16, 8, groundAlbedo: .8f));
        Assert.Equal(2, producer.Requests.Count);
    }
    #endregion

    #region Private
    /// <summary>Controls asynchronous completion while retaining the snapshots computed by the synchronous lookup.</summary>
    private sealed class ControlledProducer
    {
        #region Internal API
        /// <summary>Records each frozen worker request and its independently controlled completion.</summary>
        internal List<(AtmosphereBuildRequest Request, TaskCompletionSource<AtmosphereLighting> Completion)> Requests { get; } = [];

        /// <summary>Admits a worker request without repeating numerical integration.</summary>
        internal Task<AtmosphereLighting> Start(AtmosphereBuildRequest request, CancellationToken token)
        {
            var completion = new TaskCompletionSource<AtmosphereLighting>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Add((request, completion));
            return completion.Task;
        }

        /// <summary>Completes one request with the independently integrated lookup snapshot.</summary>
        internal void Complete(int index, AtmosphereLighting snapshot) => Requests[index].Completion.SetResult(snapshot);
        #endregion
    }
    #endregion
}
