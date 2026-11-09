using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using System.Collections.Immutable;
using System.Reflection;
using Moq;
using HarmonyLib;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Celestial;

    /// <summary>Checks CPU celestial draw admission against independent perspective bounds.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CelestialDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API


/// <summary>Storage admission includes every instance and rejects unsupported tiny or orthographic views.</summary>
    [Theory]
    [InlineData(0,1024,4)]
    [InlineData(1,2048,5)]
    [InlineData(2,3072,6)]
    [InlineData(3,4096,7)]
    public void AdmissionBoundsAllOverlapAndRejectsUnsupportedProjection(int quality,int count,int octaves)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        var source=CelestialSource.Create(new(quality,32,count,octaves));
        using var frame=TestFrameCamera.CreateFromProjection(Projection(64,1),64,64);
        // Accumulate the displayed prefix independently, preserving each FP32
        // peak before widening the sum; omitted stars belong to the galaxy map.
        double flux=0;
        foreach(var star in source.Stars.Take(count))
            flux+=star.FluxLux*Math.Max(star.Color.X,Math.Max(star.Color.Y,star.Color.Z));
        // Act: calculate the radiance admission bound for the authored source and camera.
        float bound=CelestialDrawAdmission.MaximumNits(source,frame);
        // Assert: compare the admission result with independent flux/solid-angle expectations and reject unsupported cameras.
        Assert.Equal(flux,source.ResolvedStarMaximumFluxLux);
        float p=Projection(64,1).M11;
        double corner=1.0625/p,area=4/(64*64*p*p*Math.Pow(1+2*corner*corner,1.5));
        Assert.True(bound>=source.MaximumNits+flux/area);
        using var orthographic=TestFrameCamera.CreateIdentity(64,64);
        Assert.Throws<ArgumentException>(()=>CelestialDrawAdmission.MaximumNits(source,orthographic));
        using var tiny=TestFrameCamera.CreateFromProjection(Projection(16,1),16,16);
        Assert.Throws<ArgumentException>(()=>CelestialDrawAdmission.MaximumNits(source,tiny));
    }
/// <summary>Authors a conventional perspective camera independently of the production admission calculation.</summary>
    #endregion

    #region Private
    private static Matrix4x4 Projection(int size,float fov)
    {
        float p=1/MathF.Tan(fov*.5f);
        return new(p,0,0,0,0,p,0,0,0,0,-1,-1,0,0,-.2f,0);
    }
    #endregion
}
