using System.Collections.Immutable;
using System.Numerics;
using System.Reflection;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks source lifetime invalidation separately from ordinary atmosphere motion in the retained surface producer.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class CelestialLightingAdmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Source replacement versions snapshots before admission while ordinary diffuse changes only prioritize direct refresh.</summary>
    [Fact]
    public void SourceReplacementVersionsPublishedSnapshotWithoutResettingHistoryOnMotion()
    {
        EnsureContextValid();
        var lightingProperty=typeof(AtmosphereModSystem).GetProperty("Lighting",BindingFlags.Static|BindingFlags.NonPublic)!;
        object? previous=lightingProperty.GetValue(null);
        var source=new CelestialSource(CelestialQuality.FromLevel(0),ImmutableArray<CelestialStar>.Empty,
            ImmutableArray<CelestialLightSample>.Empty,1,1,1,0);
        var lighting=new AtmosphereLighting(-Vector3.UnitY,Vector3.Zero,new(.0001f),Vector3.Zero,Vector3.Zero,[])
            {CelestialSource=source};
        try
        {
            lightingProperty.SetValue(null,lighting);
            using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
            runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame=0;
            runtime.PrimeGeometry(); runtime.RunUntil(runtime.RequestedDirectRefreshComplete);
            Assert.True(runtime.TryGetLighting(out var initial));
            var producer=runtime.LightingProvider;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var changed=(Dictionary<uint,int>)producer.GetType().GetField("changedDirectPages",flags)!.GetValue(producer)!;
            runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame=1;
            runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame=0;
            lighting=lighting with{Environment=new(.0002f),CelestialRotation=Matrix4x4.CreateRotationX(.1f)};
            lightingProperty.SetValue(null,lighting); runtime.Frame();
            Assert.True(runtime.TryGetLighting(out var moved));
            Assert.Equal(initial.DependencyRevision,moved.DependencyRevision);
            Assert.NotEmpty(changed);
            runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame=4;
            runtime.RunUntil(runtime.RequestedDirectRefreshComplete);
            runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame=0;
            lighting=lighting with{CelestialSource=source with{Quality=CelestialQuality.FromLevel(3)}};
            lightingProperty.SetValue(null,lighting); runtime.Frame();
            Assert.True(runtime.TryGetLighting(out var replaced));
            long revision=(long)producer.GetType().GetField("dependencyRevision",flags)!.GetValue(producer)!;
            Assert.True(revision>moved.DependencyRevision);
            Assert.Equal(revision,replaced.DependencyRevision);
            Assert.NotEmpty(changed);
            Assert.Same(initial.DirectIrradiance,replaced.DirectIrradiance);
            runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame=4;
            runtime.RunUntil(runtime.RequestedDirectRefreshComplete);
        }
        finally {lightingProperty.SetValue(null,previous);}
    }
    #endregion
}

