using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises actual SPIR-V blocks through the same interface calls made by engine mesh pools.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LiquidShaderProgramTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Pool interface
    /// <summary>Consecutive origin writes publish distinct GPU ranges and retain mini-dimension state.</summary>
    [Fact]
    public void PoolWritesPublishAndRestoreActualGpuParameters()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var activation = program.UseScope();
        Assert.Same(program, Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram);
        IShaderProgram engine = (IShaderProgram)Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram;
        int frameBlock = program.ProgramLayout.BinaryInterface!.GetUniformBlockIndex(LiquidFrameParamsUbo.BlockName);
        GL.GetActiveUniformBlock(program.ProgramId, frameBlock, ActiveUniformBlockParameter.UniformBlockDataSize, out int frameSize);
        Assert.Equal(LiquidFrameParamsUbo.BlockSize, frameSize);
        Assert.True(engine.HasUniform("modelViewMatrix"));
        Assert.True(engine.HasUniform("origin"));
        Assert.True(engine.HasUniform("forcedTransparency"));
        Assert.False(engine.HasUniform("mvpMatrix"));
        float[] identity = [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", .25f);
        engine.Uniform("origin", new Vec3f(1,2,3));
        var first = ReadDraw(out int firstOffset, out int firstBuffer);
        Assert.Equal(new float[] {1,2,3,.25f}, first[16..]);
        engine.Uniform("origin", new Vec3f(-4,5,6));
        var second = ReadDraw(out int secondOffset, out int secondBuffer);
        Assert.True(firstBuffer != secondBuffer || firstOffset != secondOffset);
        Assert.Equal(new float[] {-4,5,6,.25f}, second[16..]);
        // Previously submitted draws must keep their old bytes after later parameter writes.
        using (GlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, firstBuffer))
        {
            float[] retained = new float[20];
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)firstOffset, 80, retained);
            Assert.Equal(first, retained);
        }
        var moved = (float[])identity.Clone(); moved[12] = 7;
        engine.UniformMatrix("modelViewMatrix", moved);
        Assert.Equal(7, ReadDraw(out _, out _)[12]);
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", 0f);
        var restored = ReadDraw(out _, out _);
        Assert.Equal(identity, restored[..16]);
        Assert.Equal(0, restored[19]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Typed texture inputs bind every declared unit with the intended target and sampling policy.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypedImagesBindDeclaredTargetsAndSamplers(bool useInterface)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        using var activation = program.UseScope();
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var material = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var near = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var far = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var radiance = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f);
        using var attenuation = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f);
        // Exercise both the preserved engine API and the interface boundary against
        // real linked resources; targets, samplers and uniform units must be identical.
        if (useInterface)
        {
            var bindings = (ILiquidShaderProgramBindings)program;
            bindings.TerrainTexture = terrain.TextureId;
            bindings.DepthTexture = depth.TextureId;
            bindings.MaterialParamsTexture = material.TextureId;
            bindings.ShadowMapNear = near.TextureId;
            bindings.ShadowMapFar = far.TextureId;
            bindings.AerialRadianceTexture = radiance.TextureId;
            bindings.AerialAttenuationTexture = attenuation.TextureId;
        }
        else
        {
            program.TerrainTexture = terrain.TextureId;
            program.DepthTexture = depth.TextureId;
            program.MaterialParamsTexture = material.TextureId;
            program.ShadowMapNear = near.TextureId;
            program.ShadowMapFar = far.TextureId;
            program.AerialRadianceTexture = radiance.TextureId;
            program.AerialAttenuationTexture = attenuation.TextureId;
        }
        int[] images = [terrain.TextureId, depth.TextureId, material.TextureId, near.TextureId, far.TextureId, radiance.TextureId, attenuation.TextureId];
        int[] samplers = [0, GpuSamplers.NearestClamp.SamplerId, GpuSamplers.NearestClamp.SamplerId,
            GpuSamplers.ShadowCompareLinearClamp.SamplerId, GpuSamplers.ShadowCompareLinearClamp.SamplerId, 0, 0];
        int[] locations = [100, 101, 102, 49, 48, 93, 94];
        for (int unit = 0; unit < images.Length; unit++)
        {
            GlStateCache.Current.ActiveTexture(unit);
            Assert.Equal(images[unit], GL.GetInteger(unit < 5 ? GetPName.TextureBinding2D : GetPName.TextureBinding3D));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, unit, out int sampler);
            Assert.Equal(samplers[unit], sampler);
            GL.GetUniform(program.ProgramId, locations[unit], out int linkedUnit);
            Assert.Equal(unit, linkedUnit);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Staged typed frame fields reach GPU memory only at the explicit publication boundary.</summary>
    [Fact]
    public void ApplyInputsPublishesTypedFrameFields()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var activation = program.UseScope();
        program.Animation = new(1, 2, 3, 4);
        program.SetCounts(2, 1);
        program.SetPointLightPosition(1, new(5, 6, 7));
        program.SetFogSphereComponent(23, .75f);
        program.ApplyInputs();
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Frame, out int buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Frame, out int offset);
        Assert.NotEqual(0, buffer);
        byte[] bytes = new byte[LiquidFrameParamsUbo.BlockSize];
        using var binding = GlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, bytes.Length, bytes);
        Assert.Equal(new float[] { 1, 2, 3, 4 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan(192, 16)).ToArray());
        Assert.Equal(new int[] { 2, 1, 0, 0 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(bytes.AsSpan(352, 16)).ToArray());
        Assert.Equal(new float[] { 5, 6, 7 }, System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes.AsSpan(1056, 12)).ToArray());
        Assert.Equal(.75f, System.Runtime.InteropServices.MemoryMarshal.Read<float>(bytes.AsSpan(4608)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Harmony compiles the real patched methods with both new boundaries and atlas interception.</summary>
    [Fact]
    public void InstalledEngineHooksCompose()
    {
        var harmony = new HarmonyLib.Harmony("VGE.Tests.LiquidHookInstallation");
        try
        {
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.TerrainAtlasDrawBindingHook)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidRenderHooks)).Patch();
            harmony.CreateClassProcessor(typeof(VanillaGraphicsExpanded.HarmonyPatches.LiquidMeshSourceHook)).Patch();
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Reads the GPU-bound range rather than the program's CPU staging storage.</summary>
    private static float[] ReadDraw(out int offset, out int buffer)
    {
        GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Object, out buffer);
        GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Object, out offset);
        Assert.NotEqual(0, buffer);
        float[] values = new float[20];
        using var binding = GlStateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, 80, values);
        return values;
    }
    #endregion
}
