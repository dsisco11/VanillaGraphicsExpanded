using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Runs production direct and indirect composition against one generated AO receiver, isolating pre-exposure contributions by controlled draws.</summary>
internal sealed class AmbientOcclusionLightingFixture : IDisposable
{
    private readonly AmbientOcclusionSceneFixture scene;
    private readonly PBRCompositeShaderProgram composite;
    private readonly GpuFramebuffer directTarget,outputTarget;
    private readonly Texture3D directLighting;
    private readonly DynamicTexture2D indirect,zeroVisibility;
    private readonly DepthTexture shadow;
    private readonly VgeLightsUniformBuffer lights=new();
    private readonly GpuVbo positions=RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(),uv=RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
    private readonly ArrayGraphicsGeometry geometry;
    private readonly RHIGraphicsPipeline compositePipeline;

    #region Public API
    /// <summary>Evaluates actual solar BRDF output, then borrows it unchanged through all AO diagnostic compositions.</summary>
    internal AmbientOcclusionLightingFixture(ShaderTestFramework framework,ComponentShaderPrograms programs,AmbientOcclusionSceneFixture scene,
        bool lumon,float solarIrradiance,float availability)
    {
        this.scene=scene;
        positions.UploadData(new float[]{-1,-1,0,3,-1,0,-1,3,0});uv.UploadData(new float[]{0,0,2,0,0,2});
        geometry=new ArrayGraphicsGeometry(EngineFullscreenGeometry.Layout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>{[0]=positions,[1]=uv});
        directTarget=framework.CreateTestGBuffer(scene.Width,scene.Height,TextureFormat.Rgba32f,3);
        outputTarget=framework.CreateTestGBuffer(scene.Width,scene.Height,TextureFormat.Rgba32f,3);
        shadow=new DepthTexture(1,1,TextureFormat.DepthComponent32f);shadow.UploadDataImmediate([1f]);
        indirect=framework.CreateTexture(1,1,TextureFormat.Rgba32f,[availability,availability,availability,1f]);
        // A matched zero-visibility field isolates each lobe through the existing independent
        // AO controls without changing material roughness or its compensated energy budget.
        float[] zeroVisibilityData = scene.Position.ReadPixels();
        for (int offset = 0; offset < zeroVisibilityData.Length; offset += 4)
        {
            zeroVisibilityData[offset + 1] = -zeroVisibilityData[offset + 2];
            zeroVisibilityData[offset] = zeroVisibilityData[offset + 2] = zeroVisibilityData[offset + 3] = 0;
        }
        zeroVisibility=framework.CreateTexture(scene.Width,scene.Height,TextureFormat.Rgba32f,zeroVisibilityData);
        var direct=programs.Create<PBRDirectLightingShaderProgram>();
        direct.FrameInputs=scene.Camera;direct.LightsInputs=lights;direct.PrimaryScene=scene.Albedo;
        direct.PrimaryDepth=scene.Depth;direct.GBufferPosition=scene.Position;direct.GBufferSurface=scene.Surface;
        direct.ShadowMapNear=direct.ShadowMapFar=shadow;
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        direct.ToShadowMapSpaceMatrixNear=direct.ToShadowMapSpaceMatrixFar=identity;
        direct.ShadowRanges=(1,1);direct.ShadowZExtendNear=direct.ShadowZExtendFar=1;direct.DropShadowIntensity=0;
        direct.SetSolarLighting(System.Numerics.Vector3.UnitZ,new(solarIrradiance));direct.RgbaAmbientIn=new(0,0,0);
        using(var pipeline=CreatePipeline(direct,directTarget))Draw(directTarget,pipeline,direct);
        directLighting=LayeredTestTexture.Create(directTarget[0],directTarget[1],directTarget[2]);
        composite=programs.Create<PBRCompositeShaderProgram>(p=>{p.LumOnEnabled=lumon;p.EnablePbrComposite=lumon;p.EnableShortRangeAo=false;});
        composite.DirectLighting=directLighting;composite.IndirectDiffuse=indirect;
        composite.GBufferAlbedo=scene.Albedo;composite.PrimaryDepth=scene.Depth;
        composite.GBufferPosition=scene.Position;composite.GBufferSurface=scene.Surface;composite.FrameInputs=scene.Camera;
        composite.IndirectTint=new(1,1,1);composite.DiffuseAOStrength=composite.SpecularAOStrength=1;
        composite.SetAtmosphere(null);composite.SetWaterVolume(null);composite.SetUnderwater(false);
        compositePipeline=CreatePipeline(composite,outputTarget);
    }

    /// <summary>Separates direct, ambient diffuse and indirect specular from production draws without changing the retained direct-lighting buffers.</summary>
    internal (float[] Direct,float[] Diffuse,float[] Specular,float[] Combined) Capture(GpuTexture? visibility)
    {
        composite.SetAmbientOcclusion(null);
        scene.SetEnvironment(false);composite.IndirectIntensity=0;
        float[] direct=Render();
        scene.SetEnvironment(true);composite.IndirectIntensity=1;
        composite.SetAmbientOcclusion(zeroVisibility);
        composite.DiffuseAOStrength=0;composite.SpecularAOStrength=1;
        float[] diffuseBaseline=Render();
        composite.DiffuseAOStrength=1;composite.SpecularAOStrength=0;
        float[] specularBaseline=Render();

        // Keep the opposite unoccluded lobe constant, then subtract its independently captured
        // baseline. This measures visibility at the original material, rather than another BRDF.
        composite.SetAmbientOcclusion(visibility);
        float[] diffuseCombined=Render();
        composite.DiffuseAOStrength=0;composite.SpecularAOStrength=1;
        float[] specularCombined=Render();
        composite.DiffuseAOStrength=1;composite.SpecularAOStrength=1;
        float[] combined=Render();
        float[] diffuse=new float[combined.Length],specular=new float[combined.Length];
        for(int i=0;i<combined.Length;i++) {
            if(i%4==3)continue;
            diffuse[i]=diffuseCombined[i]-specularBaseline[i];
            specular[i]=specularCombined[i]-diffuseBaseline[i];
        }
        return(direct,diffuse,specular,combined);
    }

    /// <summary>Retires executable references, draw geometry and diagnostic resources without owning the borrowed scene or shaders.</summary>
    public void Dispose()
    {
        compositePipeline.Dispose();geometry.Dispose();uv.Dispose();positions.Dispose();
        directLighting.Dispose();outputTarget.Dispose();directTarget.Dispose();indirect.Dispose();zeroVisibility.Dispose();shadow.Dispose();lights.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prepares the declared three-output target signature outside the graphics boundary.</summary>
    private RHIGraphicsPipeline CreatePipeline(GpuProgram shader,GpuFramebuffer target)
    {
        var metadata = RHICommandContext.Current.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0),new(AttachmentIndex:1),new(AttachmentIndex:2)]));
        return RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,EngineFullscreenGeometry.Layout,metadata,DynamicPipelineState.Viewport)).RequireResource();
    }

    /// <summary>Submits typed fullscreen inputs inside a restoring graphics boundary, then reads only after completion.</summary>
    private float[] Render()
    {
        Draw(outputTarget,compositePipeline,composite);
        return outputTarget[0].ReadPixels();
    }

    /// <summary>Executes the actual shader and declared MRT pass with existing draw and state ownership.</summary>
    private void Draw(GpuFramebuffer target,RHIGraphicsPipeline pipeline,GpuProgram shader)
    {
        var pass=FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0),new(AttachmentIndex:1),new(AttachmentIndex:2)]);
        {
    var commands=RHICommandContext.Current;
    using var scope30=commands.EnterBoundary(new("Tests.AmbientLightingContribution",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope30.Run(()=>{
            commands.BeginRenderPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            // The fixed pipeline does not implicitly publish the shader owner's desired resources.
            commands.BindResources(shader.CaptureBindings());
            commands.Draw(geometry.Inputs,new(0,3));commands.EndRenderPass();
        });
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
