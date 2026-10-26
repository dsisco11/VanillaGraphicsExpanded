using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes native vertex face storage simultaneously with frozen lighting and material-energy storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class NativeFrameLightingBindingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Installed decal declarations retain slot three while frame decoding and energy lookup read separate slots.</summary>
    [Fact]
    public void NativeFaceDataAndFrozenLightingCoexistAcrossStages()
    {
        EnsureContextValid();
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string flags = File.ReadAllText(Path.Combine(root, "assets/game/shaderincludes/vertexflagbits.ash"));
        int first = flags.IndexOf("struct FaceData {", StringComparison.Ordinal);
        string faceType = flags[first..(flags.IndexOf("};", first, StringComparison.Ordinal) + 2)];
        string native = File.ReadAllText(Path.Combine(root, "assets/game/shaders/decals.vsh"));
        string faceBlock = native.Split('\n').Single(line => line.Contains("readonly buffer faceDataBuf", StringComparison.Ordinal));
        Assert.Contains("binding = 3", faceBlock);
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, "#version 430 core\n" + faceType + "\n" + faceBlock +
            "\nlayout(location=0)in vec2 position;flat out vec3 physical;void main(){physical=faces[0].xyz;gl_Position=vec4(position,0,1);}");
        string helper = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_frame_lighting.glsl"));
        string energy = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_microfacet_energy.glsl"));
        int fragment = shaders.Compile(ShaderType.FragmentShader, "#version 430 core\n" + energy + helper +
            "\nflat in vec3 physical;layout(location=0)out vec4 outColor;void main(){vec2 e=VgeDirectionalAlbedo(.5,1);outColor=vec4(VgeStoreFrameLight(physical)*vec3(e,1),VgeFrameLightingValid(42u,7u)?1:0);}");
        using var program = GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex, fragment));
        using var face = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.NativeFaceData" }).RequireResource();
        using var frame = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.NativeFrozenFrame" }).RequireResource();
        float[] data = new float[16]; data[0] = 11; data[1] = 13; data[2] = 17; face.UploadData(data);
        byte[] publication = new byte[32];
        MemoryMarshal.Cast<byte, float>(publication.AsSpan())[0] = .25f;
        MemoryMarshal.Cast<byte, float>(publication.AsSpan())[1] = 4;
        MemoryMarshal.Cast<byte, float>(publication.AsSpan())[2] = 100;
        MemoryMarshal.Cast<byte, float>(publication.AsSpan())[3] = 1;
        MemoryMarshal.Cast<byte, uint>(publication.AsSpan())[4] = 42;
        MemoryMarshal.Cast<byte, uint>(publication.AsSpan())[5] = 7;
        frame.UploadData(publication);
        GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, 3, out int priorFace);
        GL.GetInteger((GetIndexedPName)All.ShaderStorageBufferBinding, GpuBindingRegistry.Ssbo.FrameLighting, out int priorFrame);
        try
        {
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, face.BufferId);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, frame.BufferId);
            using var framework = new ShaderTestFramework();
            using var target = CreateMRTRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
            framework.RenderQuadTo(program.ProgramId, target);
            var lookup = MicrofacetTextureReference.Sample(MicrofacetEnergyLookup.Prepare().Width,.5f,1);
            var coefficients=lookup.Value;
            float[] actual = target[0].ReadPixels();
            float[] expected = [2.75f*coefficients.X, 3.25f*coefficients.Y, 4.25f, 1];
            float[] error=[2.75f*lookup.Error.X+.000002f,3.25f*lookup.Error.Y+.000002f,.000002f,.000002f];
            for(int channel=0;channel<4;channel++) Assert.InRange(actual[channel],expected[channel]-error[channel],expected[channel]+error[channel]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 3, priorFace);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, priorFrame);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
