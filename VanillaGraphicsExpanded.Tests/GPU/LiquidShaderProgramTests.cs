using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
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
    /// <summary>Pool draw boundaries publish staged origin writes and retain prior mini-dimension snapshots.</summary>
    [Fact]
    public void PoolWritesPublishAndRestoreActualGpuParameters()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var volume = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(program, texture, volume);
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
        LiquidPoolSubmissionHook.Prefix();
        var first = ReadDraw(out int firstOffset, out int firstBuffer);
        Assert.Equal(new float[] {1,2,3,.25f}, first[16..]);
        engine.Uniform("origin", new Vec3f(-4,5,6));
        Assert.Equal(first, ReadDraw(out _, out _));
        LiquidPoolSubmissionHook.Prefix();
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
        LiquidPoolSubmissionHook.Prefix();
        Assert.Equal(7, ReadDraw(out _, out _)[12]);
        engine.UniformMatrix("modelViewMatrix", identity);
        engine.Uniform("forcedTransparency", 0f);
        LiquidPoolSubmissionHook.Prefix();
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
        using var terrain = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba8);
        using var depth = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var material = Texture2D.Create(1, 1, PixelInternalFormat.Rgba16f);
        using var near = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var far = new DepthTexture(1, 1, PixelInternalFormat.DepthComponent32f);
        using var radiance = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
        using var attenuation = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba16f, textureTarget: TextureTarget.Texture3D);
        // Exercise both the preserved engine API and the interface boundary against
        // real linked resources; targets, samplers and uniform units must be identical.
        if (useInterface)
        {
            var bindings = (ILiquidShaderProgramBindings)program;
            bindings.TerrainTexture = terrain.TextureId;
            bindings.DepthTexture = depth.TextureId;
            bindings.MaterialParamsTexture = material;
            bindings.ShadowMapNear = near.TextureId;
            bindings.ShadowMapFar = far.TextureId;
            bindings.AerialRadianceTexture = radiance;
            bindings.AerialAttenuationTexture = attenuation;
        }
        else
        {
            program.TerrainTexture = terrain.TextureId;
            program.DepthTexture = depth.TextureId;
            program.MaterialParamsTexture = material;
            program.ShadowMapNear = near.TextureId;
            program.ShadowMapFar = far.TextureId;
            program.AerialRadianceTexture = radiance;
            program.AerialAttenuationTexture = attenuation;
        }
        using var activation = program.UseScope();
        int[] images = [terrain.TextureId, depth.TextureId, material.TextureId, near.TextureId, far.TextureId, radiance.TextureId, attenuation.TextureId];
        int[] samplers = [0, GpuSamplers.NearestClamp.SamplerId, GpuSamplers.NearestClamp.SamplerId,
            GpuSamplers.ShadowCompareLinearClamp.SamplerId, GpuSamplers.ShadowCompareLinearClamp.SamplerId, 0, 0];
        string[] names = ["terrainTex", "depthTex", "vge_materialParamsTex", "shadowMapNear", "shadowMapFar", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation"];
        for (int unit = 0; unit < images.Length; unit++)
        {
            GlStateCache.Current.ActiveTexture(unit);
            Assert.Equal(images[unit], GL.GetInteger(unit < 5 ? GetPName.TextureBinding2D : GetPName.TextureBinding3D));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, unit, out int sampler);
            Assert.Equal(samplers[unit], sampler);
            var binding = program.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
                VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler, names[unit]));
            Assert.True(binding.Active);
            Assert.Equal(unit, binding.Contract.Binding.Slot);
            Assert.DoesNotContain(names[unit], LiquidShaderProgram.Contract.Bindings.UniformLocations.Keys);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Staged typed frame fields reach GPU memory only at the explicit publication boundary.</summary>
    [Fact]
    public void UsePublishesTypedFrameFields()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        using var assets = new BinaryShaderApiFixture();
        var program = GpuShaderPrograms.Declare(assets.Api, new LiquidShaderProgram());
        Assert.True(program.EnsureReady(), string.Join("\n", assets.Logs));
        TestUniformRing.EnsureFrame();
        using var texture = Texture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var volume = DynamicTexture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32f, textureTarget: TextureTarget.Texture3D);
        AssignTextures(program, texture, volume);
        program.Animation = new(1, 2, 3, 4);
        program.SetCounts(2, 1);
        program.SetPointLightPosition(1, new(5, 6, 7));
        program.SetFogSphereComponent(23, .75f);
        using var activation = program.UseScope();
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
            harmony.CreateClassProcessor(typeof(LiquidPoolSubmissionHook)).Patch();
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Supplies required borrowed textures for buffer-only tests that issue no draw.</summary>
    private static void AssignTextures(LiquidShaderProgram program, Texture2D texture, DynamicTexture3D volume)
    {
        program.TerrainTexture = texture.TextureId;
        program.DepthTexture = texture.TextureId;
        program.MaterialParamsTexture = texture;
        program.ShadowMapNear = texture.TextureId;
        program.ShadowMapFar = texture.TextureId;
        program.AerialRadianceTexture = volume;
        program.AerialAttenuationTexture = volume;
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
