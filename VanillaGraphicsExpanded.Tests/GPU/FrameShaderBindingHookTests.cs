using HarmonyLib;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
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
        using var publisher = new VgeFrameRenderer(api);
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
            StateCache.Current.BindBufferBase(BufferRangeTarget.UniformBuffer,GpuBindingRegistry.Ubo.Frame,0);
            StateCache.Current.BindBufferBase(BufferRangeTarget.UniformBuffer,GpuBindingRegistry.Ubo.Lights,0);
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
            Assert.True(foreign.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
            var unrelated = Binding();
            Assert.Throws<InvalidOperationException>(() => VgeFrameRenderer.Current);
            engine.Use();
            Assert.Equal(unrelated,Binding());
            events.Render(EnumRenderStage.Before);
            events.Render(EnumRenderStage.Opaque);
            byte[] expected = VgeFrameRenderer.Current.Bytes.ToArray();
            Assert.True(foreign.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
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
                long lightCopies = ring.AllocationsWritten;
                engine.Use();
                Assert.Equal(lightCopies,ring.AllocationsWritten);
                // Resizing captures a fresh generation; the previously submitted ring slice stays immutable.
                identity[0]=2;
                Mock.Get(render).SetupGet(value => value.FrameWidth).Returns(32);
                Mock.Get(render).SetupGet(value => value.FrameHeight).Returns(16);
                render.ShaderUniforms.PointLightColors3[0] = .5f;
                lightsPublisher.OnRenderFrame(.032f,EnumRenderStage.Before);
                publisher.OnRenderFrame(.032f,EnumRenderStage.Before);
                publisher.OnRenderFrame(.032f,EnumRenderStage.Opaque);
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
            harmony.UnpatchAll(harmony.Id);
            FrameShaderBindingHook.ClearUniformCache();
            ShaderProgramBase.CurrentShaderProgram=previous;
            StateCache.Current.UseProgram(0);
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
            uniformLocations["vge_pbrRoute"]=GpuProgramLayout.TryBuild(program).GetUniformLocation(program,"vge_pbrRoute");
        }
        #endregion
    }
    #endregion
}
