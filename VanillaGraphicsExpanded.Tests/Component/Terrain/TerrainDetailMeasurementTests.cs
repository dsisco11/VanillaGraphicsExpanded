using VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;



using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Measures production stage assets on a controlled, explicitly synthetic terrain workload.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class TerrainDetailMeasurementTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private readonly VanillaGraphicsExpanded.Tests.Support.Cache.CountingDiskCacheFileSystem reports = new(DiagnosticReports.Publish);
    #region Public API
    #region Measurement
    /// <summary>Records three warmed ABBA blocks per comparison with identical draws and no timing assertions.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void MatchedProductionStages()
    {
        // Arrange: opt in explicitly before allocating or warming the diagnostic workload.
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_TERRAIN_DETAIL_MEASUREMENTS")=="1", "Explicit measurement run required.");
        EnsureContextValid();
        using var scope = new TerrainDetailWorkload();
        var rows=new List<object>();
        foreach(string candidate in new[]{"adaptiveDisabled","adaptiveNear","adaptiveFaded","adaptiveNeutral","relief"})
        {
            string baseline=candidate=="relief" ? "reliefOff" : "triangles";
            foreach(string mode in new[]{baseline,candidate}) { scope.Select(mode); for(int i=0;i<8;i++)scope.Draw(); }
            GpuTestFence.WaitForGpuOrSkip("Terrain measurement warmup");
            // Act: record three warmed ABBA blocks using identical draw counts.
            for(int block=0;block<3;block++) foreach(string mode in new[]{baseline,candidate,candidate,baseline})
            {
                scope.Select(mode);
                using var timer=GpuTimerQuery.Create();
                using var primitives=RHICommandContext.Current.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
                GL.BeginQuery(QueryTarget.PrimitivesGenerated,primitives.QueryId);
                timer.Begin();for(int draw=0;draw<16;draw++)scope.Draw();timer.End();
                GL.EndQuery(QueryTarget.PrimitivesGenerated);
                GpuTestFence.WaitForGpuOrSkip("Terrain measurement completion");
                // Assert each measured draw completed and emitted primitives before recording its result.
                Assert.True(timer.TryGetResultNanoseconds(out long nanos));
                GL.GetQueryObject(primitives.QueryId,GetQueryObjectParam.QueryResult,out long generated);
                Assert.True(generated>0);
                rows.Add(new{candidate,block,mode,gpuMilliseconds=nanos/1e6,generatedPrimitives=generated,draws=16,instances=16,inputTrianglesPerInstance=2});
            }
        }
        string path=Environment.GetEnvironmentVariable("VGE_TERRAIN_DETAIL_REPORT") ?? Path.Combine(AppContext.BaseDirectory,"terrain-detail-measurement.json");
        reports.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        reports.WriteAllText(path,JsonSerializer.Serialize(new{renderer=GL.GetString(StringName.Renderer),vendor=GL.GetString(StringName.Vendor),version=GL.GetString(StringName.Version),width=256,height=256,targetEdgePixels=8,maximumLevel=8,effectiveOddCap=7,focalPixels=256,amplitudeMetres=.04,nearFade=new[]{10,20},fadedFade=new[]{0,.1},heightSample=0,neutralHeight=.5,overdrawPerSample=256,description="Synthetic saturated full-screen terrain faces; production TCS/TES and relief include; excludes actual chunks, shadows, material binding and CPU traversal",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
    #endregion
    #endregion

}
