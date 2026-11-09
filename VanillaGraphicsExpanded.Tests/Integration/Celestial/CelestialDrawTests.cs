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

namespace VanillaGraphicsExpanded.Tests.Integration.Celestial;

    /// <summary>Checks persistent instance geometry against the live graphics context and disposal boundary.</summary>
    [Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CelestialDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API


/// <summary>Persistent instance geometry exposes exactly the selected prefix and retires with the completed source owner.</summary>
    [Fact]
    public void InstanceGeometryRetainsSelectedPrefixUntilDisposal()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        var source=CelestialSource.Create(new(0,32,1024,4));
        using var stars=new CelestialStarGeometry(source);
        // Act and assert: Persistent instance geometry exposes exactly the selected prefix and retires with the completed source owner.
        Assert.Equal(1024,stars.Count);
        using var shaders=new GraphicsStateShaders();
        var shader=shaders.Get();
        using var target=CreateMRTRenderTarget(1,1,TextureFormat.Rgba32f,TextureFormat.Rgba32f);
        var context=RHICommandContext.Current;
        using var pipeline=context.CreateGraphicsPipeline(new(shader,CelestialStarGeometry.Layout,
            context.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(target,[new(AttachmentIndex:0),new(AttachmentIndex:1)])),DynamicPipelineState.Viewport)).RequireResource();
        using(var boundary=context.EnterBoundary(new("Tests.StarInstanceBounds",true,graphics:[pipeline],operations:BoundaryOperations.RenderPasses)))
            boundary.Run(()=>
            {
                context.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target,[new(AttachmentIndex:0),new(AttachmentIndex:1)]));
                context.SetPipeline(pipeline);context.SetDynamicState(new(){Viewport=context.PassViewport});context.BindResources(new(shader));
                context.Draw(stars.Geometry.Inputs,new(0,6,InstanceCount:stars.Count));
                Assert.Throws<ArgumentOutOfRangeException>(()=>context.Draw(stars.Geometry.Inputs,new(0,6,InstanceCount:stars.Count+1)));
                context.EndRenderPass();
            });
        stars.Dispose();Assert.Throws<ObjectDisposedException>(()=>{_=stars.Geometry.Inputs;});
    }
    #endregion
}
