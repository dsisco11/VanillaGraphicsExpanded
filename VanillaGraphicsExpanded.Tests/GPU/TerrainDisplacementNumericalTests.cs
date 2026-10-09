using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production displacement functions with controlled atlas samples and projection inputs.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class TerrainDisplacementNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainDisplacementNumericalTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Bounded shader functions
    /// <summary>The production angular metric is symmetric, bounded near the eye and fades with camera distance.</summary>
    [Theory]
    [InlineData(.01f)]
    [InlineData(8f)]
    [InlineData(16f)]
    [InlineData(24f)]
    public void ProductionAngularMetricMatchesCameraDistance(float distance)
    {
        EnsureContextValid();
        using var inputs = new PackedUniformBuffer(176);
        byte[] inputBytes = new byte[176];
        using var camera = TestFrameCamera.CreateIdentity(128,128);
        Assert.True(camera.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
        using var shaders=new TerrainShaderTestFixture();
        int vertex=shaders.Load(ShaderType.VertexShader,"tests/complete-state.vsh");
        int fragment=shaders.Load(ShaderType.FragmentShader,"tests/displacement-metric.fsh");
        using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fragment));
        using var vao=GpuVao.Create();using var framework=new ShaderTestFramework();
        using var target=framework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        int id=program.ProgramId;var layout=BuiltShaderFixture.Layout(id,"tests/displacement-metric.fsh");
        target.BindWithViewport();StateCache.Current.UseProgram(id);StateCache.Current.BindVertexArray(vao.VertexArrayId);
        UboPacking.WriteFloat(inputBytes, 172,distance);
        UboPacking.WriteFloat(inputBytes, 128,1024f);
        UboPacking.WriteVec2(inputBytes, 136,8f,8f);
        UboPacking.WriteVec2(inputBytes, 144,8f,24f);
        GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.Blend);GL.Disable(EnableCap.CullFace);
        inputs.SetBytes(inputBytes);
        Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
        GL.DrawArrays(PrimitiveType.Triangles,0,3);float[] result=target[0].ReadPixels();
        float radial=MathF.Sqrt(distance*distance+.25f),t=Math.Clamp((radial-8f)/16f,0f,1f);
        float expected=1f+(Math.Clamp(128f/Math.Max(.05f,radial),1f,7f)-1f)*(1f-t*t*(3f-2f*t));
        Assert.InRange(result[0],expected-.00001f,expected+.00001f);
        Assert.Equal(result[0],result[1]);Assert.Equal(1f,result[2]);Assert.InRange(result[0],1f,7f);
        StateCache.Current.UseProgram(0);StateCache.Current.BindVertexArray(0);
    }

    /// <summary>Shadow subdivision stays bounded without a world camera when its frozen angular metric is unavailable.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    public void ShadowWithoutAngularMetricUsesConservativeCap(float focalPixels)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader,"#version 430 core\nlayout(location=0) in vec2 position; void main(){gl_Position=vec4(position,0,1);}");
        string directory = Path.Combine(AppContext.BaseDirectory,"assets","shaders","includes");
        string contract = File.ReadAllText(Path.Combine(directory,"tests","displacement_inputs.glsl"));
        string kernel = TerrainEyeRelativeShadingTests.Expand(Path.Combine(directory,"tessellation","terrain_displacement.glsl"));
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 430 core\n#define VGE_TESS_SHADOW 1\n" + contract + kernel + """
            out vec4 result;
            void main() {
                vec3 a=vec3(-.5,0,-2),b=vec3(.5,0,-2);
                result=vec4(VgeEdgeLevel(a,b),VgeEdgeLevel(b,a),VgeEdgeLevel(vec3(0,0,-30),vec3(1,0,-30)),1);
            }
            """);
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fragment));
        using var inputs = new PackedUniformBuffer(176);
        byte[] bytes = new byte[176];
        UboPacking.WriteFloat(bytes,128,focalPixels);
        UboPacking.WriteVec2(bytes,136,8f,8f);
        UboPacking.WriteVec2(bytes,144,8f,24f);
        inputs.SetBytes(bytes);
        Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
        // A shadow pass precedes world publication and must not activate the shared camera block.
        StateCache.Current.BindBufferBase(BufferRangeTarget.UniformBuffer,GpuBindingRegistry.Ubo.Frame,0);
        Assert.Equal(-1,GL.GetUniformBlockIndex(program.ProgramId,"VgeFrameUBO"));
        using var framework = new ShaderTestFramework();
        using var target = framework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
        framework.RenderQuadTo(program.ProgramId,target);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        Assert.Equal(new float[] {7,7,1,1},target[0].ReadPixels());
    }

    /// <summary>Height mapping is signed and bounded; neutral, boundary and invalid samples remain undisplaced.</summary>
    [Theory]
    [InlineData(.5f,.25f,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,.04f,.04f)]
    [InlineData(0f,.25f,.5f,.04f,-.04f)]
    [InlineData(1f,0f,.5f,.04f,0f)]
    [InlineData(1f,.5f,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,.5f,.05f)]
    [InlineData(float.NaN,.25f,.5f,.04f,0f)]
    [InlineData(float.PositiveInfinity,.25f,.5f,.04f,0f)]
    [InlineData(1f,.49f,.5f,.04f,0f)]
    [InlineData(1f,.4f,.5f,.04f,.00864f)]
    [InlineData(1f,float.NaN,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,float.NaN,0f)]
    public void HeightAndSharedEdgeRespectBounds(float height,float u,float v,float amplitude,float expected)
    {
        EnsureContextValid();
        using var inputs = new PackedUniformBuffer(176);
        byte[] inputBytes = new byte[176];
        using var camera = TestFrameCamera.CreateIdentity(128,128);
        Assert.True(camera.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
        using var shaders = new TerrainShaderTestFixture();
        int vertex=shaders.Load(ShaderType.VertexShader,"tests/complete-state.vsh");
        int fragment=shaders.Load(ShaderType.FragmentShader,"tests/displacement-height.fsh");
        int program=TerrainShaderTestFixture.Link(vertex,fragment);
        using var vertexArray = GpuVao.Create();
        int vao = vertexArray.VertexArrayId;
        try
        {
            using var framework=new ShaderTestFramework();
            var data=new float[16*16*4];
            for(int y=0;y<16;y++) for(int x=0;x<16;x++) data[(y*16+x)*4+3]=x<8?height:1-height;
            using var atlas=framework.CreateTexture(16,16,PixelInternalFormat.Rgba32f,data);
            using var target=framework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
            var layout = BuiltShaderFixture.Layout(program,"tests/displacement-height.fsh");
            target.BindWithViewport(); StateCache.Current.UseProgram(program); StateCache.Current.BindVertexArray(vao);
            atlas.Bind(0); ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_normalDepthTex"),0);
            UboPacking.WriteVec3(inputBytes, 160,u,v,amplitude);
            UboPacking.WriteVec2(inputBytes, 144,10f,20f);
            UboPacking.WriteVec2(inputBytes, 136,16f,8f);
            float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(identity.AsSpan()).CopyTo(inputBytes.AsSpan(64));
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            inputs.SetBytes(inputBytes);
            Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
            GL.DrawArrays(PrimitiveType.Triangles,0,3);
            var actual=target[0].ReadPixels();
            Assert.InRange(actual[0],expected-.00001f,expected+.00001f);
            Assert.Equal(4f,actual[1]); Assert.Equal(actual[1],actual[2]); Assert.Equal(1f,actual[3]);
        }
        finally { StateCache.Current.UseProgram(0); StateCache.Current.BindVertexArray(0);  GpuProgramObject.Adopt(program).Dispose();   }
    }
    #endregion
}
