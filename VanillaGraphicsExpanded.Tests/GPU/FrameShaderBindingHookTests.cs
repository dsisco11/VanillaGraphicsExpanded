using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native shader binding boundaries against actual shared snapshots and retained driver ranges.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FrameShaderBindingHookTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>GUI and offscreen use skip absent world publication, while nested solar scopes admit the real shared camera.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeRoutesSkipWorldCameraUntilSolarScope(bool offscreen)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var world = new Mock<IClientWorldAccessor>();
        world.SetupGet(value => value.Player).Returns(RuntimeEngineServices.CameraPlayer(() => new LumOnCameraState(0,0,0,0,0,0,0)));
        world.SetupGet(value => value.ElapsedMilliseconds).Returns(0L);
        float[] identity = Vintagestory.API.MathTools.Mat4f.Create();
        var buffers = Enumerable.Range(0,32).Select(index => new FrameBufferRef { FboId=100+index, Width=1, Height=1 }).ToList();
        var render = RuntimeEngineServices.Render(1,buffers,() => identity,() => identity,() => { });
        Mock.Get(render).SetupGet(value => value.CurrentRenderStage).Returns(offscreen ? EnumRenderStage.Opaque : EnumRenderStage.Ortho);
        Mock.Get(render).SetupGet(value => value.CurrentFrameBuffer).Returns(offscreen ? new FrameBufferRef { FboId=999 } : buffers[(int)EnumFrameBuffer.Primary]);
        var api = RuntimeEngineServices.Client(assets.Api,events.Api,world.Object,render,assets.Api.Shader);
        // The shared camera also owns decoded engine light-table calibration; supply
        // its actual native world boundary rather than an interface-only world mock.
        var cameraWorld=new RuntimeCameraWorld(RuntimeEngineServices.CameraPlayer(()=>new LumOnCameraState(0,0,0,0,0,0,0)));
        using var publisher = new VgeFrameRenderer(RuntimeEngineServices.Client(api,events.Api,cameraWorld.World,render,assets.Api.Shader));
        using var lightsPublisher = new VgeLightsRenderer(api);
        render.ShaderUniforms.PointLightsCount = 1;
        render.ShaderUniforms.PointLightColors3[0] = .25f;
        using var shaders = new TerrainShaderTestFixture();
        string directory = Path.Combine(AppContext.BaseDirectory,"assets","shaders","includes");
        string schema = File.ReadAllText(Path.Combine(directory,"vge_frame_ubo.glsl"))
            .Replace("@import \"./vge_ubo_bindings.glsl\"",File.ReadAllText(Path.Combine(directory,"vge_ubo_bindings.glsl")));
        int vertex = shaders.Compile(ShaderType.VertexShader,"#version 430 core\n" + schema + "\nlayout(location=0) in vec2 position; uniform int vge_pbrRoute; void main(){vec4 clip=vec4(position,0,1); gl_Position=vge_pbrRoute==0 ? clip : vgeFrame.currViewProjMatrix*clip;}");
        string lightSchema = File.ReadAllText(Path.Combine(directory,"vge_lights_ubo.glsl"))
            .Replace("@import \"./vge_ubo_bindings.glsl\"",File.ReadAllText(Path.Combine(directory,"vge_ubo_bindings.glsl")));
        int fragment = shaders.Compile(ShaderType.FragmentShader,"#version 430 core\n" + lightSchema + "\nuniform int vge_pbrRoute; out vec4 color; void main(){color=vge_pbrRoute==0 ? vec4(1) : vec4(vgeLights.colors[0].rgb,float(vgeLights.lightCount));}");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fragment));
        var engine = new FrameConsumerProgram(program.ProgramId);
        var harmony = new Harmony("tests.frame-native."+Guid.NewGuid());
        var previous = ShaderProgramBase.CurrentShaderProgram;
        FrameShaderBindingHook.ApplyPatches(harmony,_ => { });
        FrameShaderBindingHook.ClearUniformCache();
        try
        {
            // Native GUI/offscreen routes must draw before world publication with no camera range inherited.
            OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.UniformBuffer,GpuBindingRegistry.Ubo.Frame,0);
            OpenGLBufferBindings.BindBufferBase(StateCache.Current, BufferRangeTarget.UniformBuffer,GpuBindingRegistry.Ubo.Lights,0);
            Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
            engine.Use();
            Assert.Equal(0,Binding().Buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferBinding,GpuBindingRegistry.Ubo.Lights,out int absentLights);
            Assert.Equal(0,absentLights);
            using var framework = new ShaderTestFramework();
            using var target = framework.CreateTestGBuffer(2,2,PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program.ProgramId,target);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            Assert.All(target[0].ReadPixels(),value => Assert.Equal(1f,value));
            Assert.Equal(0,Binding().Buffer);
            Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
            using var foreign = new PackedUniformBuffer(VgeFrameUniformBuffer.PackedSize);
            foreign.SetBytes(new byte[VgeFrameUniformBuffer.PackedSize]);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.BindEngineUniform(GpuBindingRegistry.Ubo.Frame, foreign.Capture()));
            var unrelated = Binding();
            Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
            engine.Use();
            Assert.Equal(unrelated,Binding());
            events.Render(EnumRenderStage.Before);
            events.Render(EnumRenderStage.Opaque);
            byte[] expected = VgeFrameRenderer.Current.Bytes.ToArray();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.BindEngineUniform(GpuBindingRegistry.Ubo.Frame, foreign.Capture()));
            AtmosphereSunDrawHook.Prefix(out bool outer);
            try
            {
                AtmosphereSunDrawHook.Prefix(out bool nested);
                try { engine.Use(); }
                finally { AtmosphereSunDrawHook.Finalizer(nested); }
                Assert.True(AtmosphereSunDrawHook.Active);
                var selected = Binding();
                Assert.NotEqual(unrelated,selected);
                Assert.Equal(expected,ReadSnapshot(selected));
                GL.GetInteger(GetIndexedPName.UniformBufferBinding,GpuBindingRegistry.Ubo.Lights,out int lightBuffer);
                Assert.NotEqual(0,lightBuffer);
                var firstLights = Binding(GpuBindingRegistry.Ubo.Lights);
                byte[] expectedLights = VgeLightsRenderer.Current.Bytes.ToArray();
                Assert.Equal(expectedLights,ReadSnapshot(firstLights));
                Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
#if DEBUG
                long lightCopies = ring.AllocationsWritten;
#endif
                engine.Use();
#if DEBUG
                Assert.Equal(lightCopies,ring.AllocationsWritten);
#endif
                // Resizing captures a fresh generation; the previously submitted ring slice stays immutable.
                identity[0]=2;
                Mock.Get(render).SetupGet(value => value.FrameWidth).Returns(32);
                Mock.Get(render).SetupGet(value => value.FrameHeight).Returns(16);
                render.ShaderUniforms.PointLightColors3[0] = .5f;
                lightsPublisher.OnRenderFrame(.032f,EnumRenderStage.Before);
                publisher.BeginFrame();
                publisher.OnRenderFrame(.032f,EnumRenderStage.Before);
                lightsPublisher.OnRenderFrame(.032f,EnumRenderStage.Opaque);
                FrameShaderBindingHook.ClearUniformCache();
                engine.Use();
                var resized = Binding();
                var newLights = Binding(GpuBindingRegistry.Ubo.Lights);
                Assert.NotEqual(firstLights,newLights);
                Assert.Equal(expectedLights,ReadSnapshot(firstLights));
                Assert.Equal(VgeLightsRenderer.Current.Bytes.ToArray(),ReadSnapshot(newLights));
                byte[] resizedBytes = VgeFrameRenderer.Current.Bytes.ToArray();
                Assert.NotEqual(selected,resized);
                Assert.Equal(expected,ReadSnapshot(selected));
                Assert.Equal(resizedBytes,ReadSnapshot(resized));
                engine.Use();
                Assert.Equal(resized,Binding());
                Assert.Equal(2f,System.Runtime.InteropServices.MemoryMarshal.Read<float>(resizedBytes));
                Assert.Equal(32f,System.Runtime.InteropServices.MemoryMarshal.Read<float>(resizedBytes.AsSpan(384)));
                Assert.Equal(16f,System.Runtime.InteropServices.MemoryMarshal.Read<float>(resizedBytes.AsSpan(388)));
                Assert.Equal(.032f,System.Runtime.InteropServices.MemoryMarshal.Read<float>(resizedBytes.AsSpan(504)));
            }
            finally { AtmosphereSunDrawHook.Finalizer(outer); }
            Assert.False(AtmosphereSunDrawHook.Active);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);FrameShaderBindingHook.ClearUniformCache();
            ShaderProgramBase.CurrentShaderProgram=previous;
            OpenGLProgramSelection.Select(StateCache.Current, 0);
        }
    }

    /// <summary>The actual engine-use hook restores filtered unit32 after foreign bindings and live quality replacement.</summary>
    [Fact]
    public void NativeEnergySamplerTracksQualityWithoutMembershipReset()
    {
        EnsureContextValid();
        using var platform=new EngineShaderPlatformScope();
        using var shaders=new TerrainShaderTestFixture();
        string helper=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/shaders/includes/pbr_microfacet_energy.glsl"));
        int vertex=shaders.Compile(ShaderType.VertexShader,"#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        int fragment=shaders.Compile(ShaderType.FragmentShader,"#version 330 core\n"+helper+
            "\nlayout(location=0)out vec4 result;void main(){result=vec4(VgeDirectionalAlbedo(.37,.003),0,1);}");
        using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fragment));
        using var target=CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);
        var engine=new FrameConsumerProgram(program.ProgramId);
        var harmony=new Harmony("tests.energy-native."+Guid.NewGuid());
        var previousProgram=ShaderProgramBase.CurrentShaderProgram;
        int previousQuality=ConfigModSystem.Config.ShadingQuality;
        FrameShaderBindingHook.ApplyPatches(harmony,_=>{});
        FrameShaderBindingHook.ClearUniformCache();
        try
        {
            GL.GetInteger(GetPName.MaxCombinedTextureImageUnits,out int units);
            Assert.True(units>GpuBindingRegistry.Sampler.MicrofacetEnergy);
            GL.GetProgram(program.ProgramId,GetProgramParameterName.ActiveUniforms,out int activeUniforms);
            GL.GetInteger(GetPName.MaxTextureImageUnits,out int fragmentSamplers);
            Assert.Equal(1,activeUniforms);
            Assert.True(activeUniforms<=fragmentSamplers);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,target.FboId);GL.Viewport(0,0,1,1);
            GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.Blend);GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
            foreach(int quality in new[]{3,0,1,2,3})
            {
                ConfigModSystem.Config.ShadingQuality=quality;
                int unit=GpuBindingRegistry.Sampler.MicrofacetEnergy;
                OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture2D,unit,0);
                OpenGLImageBindings.BindSampler(StateCache.Current, unit,0);
                engine.Use();
                var selected=MicrofacetEnergyLookup.Prepare();
                GL.GetInteger(GetPName.ActiveTexture,out int priorActive);
                int binding;
                // Inspect without binding: a texture scope here would conceal a missing hook.
                GL.ActiveTexture(TextureUnit.Texture0+unit);
                try { GL.GetInteger(GetPName.TextureBinding2D,out binding); }
                finally { GL.ActiveTexture((TextureUnit)priorActive); }
                Assert.Equal(selected.TextureId,binding);
                GL.GetInteger((GetIndexedPName)All.SamplerBinding,unit,out int sampler);
                Assert.Equal(GpuSamplers.LinearClamp.SamplerId,sampler);
                // Draw directly: a general test framework must not repair a missing hook binding.
                RenderFullscreenQuad(program.ProgramId);
                var expected=MicrofacetTextureReference.Sample(selected.Width,.37f,.003f);
                var actual=ReadPixelsFloat(target);
                Assert.InRange(Math.Abs(actual[0]-expected.Value.X),0,expected.Error.X);
                Assert.InRange(Math.Abs(actual[1]-expected.Value.Y),0,expected.Error.Y);
                Assert.Equal(1,actual[3]);
                Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
        }
        finally
        {
            ConfigModSystem.Config.ShadingQuality=previousQuality;
            MicrofacetEnergyLookup.Prepare();
            harmony.UnpatchAll(harmony.Id);FrameShaderBindingHook.ClearUniformCache();
            ShaderProgramBase.CurrentShaderProgram=previousProgram;OpenGLProgramSelection.Select(StateCache.Current, 0);
        }
    }
    #endregion
    #region Private
    /// <summary>Reads the actual retained slice selected at a shared frame or light slot.</summary>
    private static (int Buffer,int Offset,int Size) Binding(int slot = GpuBindingRegistry.Ubo.Frame)
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding,slot,out int buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart,slot,out int offset);
        GL.GetInteger(GetIndexedPName.UniformBufferSize,slot,out int size);
        return (buffer,offset,size);
    }
    /// <summary>Inspects a retained GPU snapshot without changing its owning publication or camera state.</summary>
    private static byte[] ReadSnapshot((int Buffer,int Offset,int Size) selected)
    {
        using var scope = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer,selected.Buffer);
        byte[] bytes = new byte[selected.Size];
        GL.GetBufferSubData(BufferTarget.UniformBuffer,(IntPtr)selected.Offset,bytes.Length,bytes);
        return bytes;
    }
    /// <summary>Populates the native uniform lookup table for the actual linked route-aware program.</summary>
    private sealed class FrameConsumerProgram : ShaderProgram
    {
        #region Public API
        /// <summary>Uses the linked graphics interface to retain the route uniform's native location.</summary>
        internal FrameConsumerProgram(int program)
        {
            ProgramId=program;
            int route=GpuProgramLayout.TryBuild(program).GetUniformLocation(program,"vge_pbrRoute");
            if(route>=0)uniformLocations["vge_pbrRoute"]=route;
        }
        #endregion
    }
    #endregion
}
