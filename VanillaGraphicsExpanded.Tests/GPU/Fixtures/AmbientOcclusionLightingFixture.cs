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
    private readonly DynamicTexture2D indirect;
    private readonly DepthTexture shadow;
    private readonly VgeLightsUniformBuffer lights=new();
    private readonly GpuVbo positions=GpuVbo.Create(),uv=GpuVbo.Create();
    private readonly ArrayGraphicsGeometry geometry;
    private readonly GraphicsPipelineLifetime lifetime=new();
    private readonly GraphicsPipeline compositePipeline;

    #region Public API
    /// <summary>Evaluates actual solar BRDF output, then borrows it unchanged through all AO diagnostic compositions.</summary>
    internal AmbientOcclusionLightingFixture(ShaderTestFramework framework,ComponentShaderPrograms programs,AmbientOcclusionSceneFixture scene,
        bool lumon,float solarIrradiance,float availability)
    {
        this.scene=scene;
        positions.UploadData(new float[]{-1,-1,0,3,-1,0,-1,3,0});uv.UploadData(new float[]{0,0,2,0,0,2});
        geometry=new ArrayGraphicsGeometry(EngineFullscreenGeometry.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>{[0]=positions,[1]=uv});
        directTarget=framework.CreateTestGBuffer(scene.Width,scene.Height,PixelInternalFormat.Rgba32f,3);
        outputTarget=framework.CreateTestGBuffer(scene.Width,scene.Height,PixelInternalFormat.Rgba32f,3);
        shadow=new DepthTexture(1,1,PixelInternalFormat.DepthComponent32f);shadow.UploadDataImmediate([1f]);
        indirect=framework.CreateTexture(1,1,PixelInternalFormat.Rgba32f,[availability,availability,availability,1f]);
        var direct=programs.Create<PBRDirectLightingShaderProgram>();
        direct.FrameInputs=scene.Camera;direct.LightsInputs=lights;direct.PrimaryScene=scene.Albedo.TextureId;
        direct.PrimaryDepth=scene.Depth.TextureId;direct.GBufferPosition=scene.Position.TextureId;direct.GBufferSurface=scene.Surface;
        direct.ShadowMapNear=direct.ShadowMapFar=shadow.TextureId;
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        direct.ToShadowMapSpaceMatrixNear=direct.ToShadowMapSpaceMatrixFar=identity;
        direct.ShadowRanges=(1,1);direct.ShadowZExtendNear=direct.ShadowZExtendFar=1;direct.DropShadowIntensity=0;
        direct.SetSolarLighting(System.Numerics.Vector3.UnitZ,new(solarIrradiance));direct.RgbaAmbientIn=new(0,0,0);
        using(var pipeline=CreatePipeline(direct,directTarget))Draw(directTarget,pipeline);
        directLighting=LayeredTestTexture.Create(directTarget[0],directTarget[1],directTarget[2]);
        composite=programs.Create<PBRCompositeShaderProgram>(p=>{p.LumOnEnabled=lumon;p.EnablePbrComposite=lumon;p.EnableShortRangeAo=false;});
        composite.DirectLighting=directLighting;composite.IndirectDiffuse=indirect;
        composite.GBufferAlbedo=scene.Albedo.TextureId;composite.PrimaryDepth=scene.Depth.TextureId;
        composite.GBufferPosition=scene.Position.TextureId;composite.GBufferSurface=scene.Surface;composite.FrameInputs=scene.Camera;
        composite.IndirectTint=new(1,1,1);composite.DiffuseAOStrength=composite.SpecularAOStrength=1;
        composite.SetAtmosphere(null);composite.SetWaterVolume(null);composite.SetUnderwater(false);
        compositePipeline=CreatePipeline(composite,outputTarget);
    }

    /// <summary>Separates direct, ambient diffuse and indirect specular from production draws without changing the retained direct-lighting buffers.</summary>
    internal (float[] Direct,float[] Diffuse,float[] Specular,float[] Combined) Capture(GpuTexture? visibility)
    {
        composite.SetAmbientOcclusion(visibility);
        scene.SetCompositeRoughness(false);scene.SetEnvironment(false);composite.IndirectIntensity=0;
        float[] direct=Render();
        scene.SetEnvironment(true);composite.IndirectIntensity=1;
        // Integrated diffuse Fresnel does not depend on roughness. Raising only composite roughness removes its specular lobe.
        scene.SetCompositeRoughness(true);float[] diffuseCombined=Render();
        scene.SetCompositeRoughness(false);float[] combined=Render();
        float[] diffuse=new float[combined.Length],specular=new float[combined.Length];
        for(int i=0;i<combined.Length;i++) {
            if(i%4==3)continue;
            diffuse[i]=diffuseCombined[i]-direct[i];specular[i]=combined[i]-diffuseCombined[i];
        }
        return(direct,diffuse,specular,combined);
    }

    /// <summary>Retires executable references, draw geometry and diagnostic resources without owning the borrowed scene or shaders.</summary>
    public void Dispose()
    {
        compositePipeline.Dispose();lifetime.Dispose();geometry.Dispose();uv.Dispose();positions.Dispose();
        directLighting.Dispose();outputTarget.Dispose();directTarget.Dispose();indirect.Dispose();shadow.Dispose();lights.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prepares the declared three-output target signature outside the graphics boundary.</summary>
    private GraphicsPipeline CreatePipeline(GpuProgram shader,GpuFramebuffer target)
    {
        using var metadata=new RenderPassTargets(new RenderPassDesc(target,[new(0),new(1),new(2)]));
        return new GraphicsPipeline(lifetime,new(shader.GraphicsIdentity!,EngineFullscreenGeometry.Layout,metadata.Signature,DynamicPipelineState.Viewport),shader);
    }

    /// <summary>Submits typed fullscreen inputs inside a restoring graphics boundary, then reads only after completion.</summary>
    private float[] Render()
    {
        Draw(outputTarget,compositePipeline);
        return outputTarget[0].ReadPixels();
    }

    /// <summary>Executes the actual shader and declared MRT pass with existing draw and state ownership.</summary>
    private void Draw(GpuFramebuffer target,GraphicsPipeline pipeline)
    {
        var pass=new RenderPassDesc(target,[new(0),new(1),new(2)]);
        Assert.True(GraphicsCommandContext.TryRun("Tests.AmbientLightingContribution",[pipeline],true,commands=>{
            commands.BeginPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry,new(0,3));commands.EndPass();
        }));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
