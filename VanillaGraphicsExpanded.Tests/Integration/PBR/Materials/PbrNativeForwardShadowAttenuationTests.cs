using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.PBR.Materials;

/// <summary>Executes an installed patched native forward fragment against a current VSM page publication.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PbrNativeForwardShadowAttenuationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A complete current sun page suppresses direct light in installed opaque and terrain-transparent forward families.</summary>
    [Theory]
    [InlineData("standard")]
    [InlineData("entityanimated")]
    [InlineData("instanced")]
    [InlineData("chunktransparent")]
    public void InstalledForwardFragmentAttenuatesSunWithPublishedVsmPage(string family)
    {
        // Arrange: Use a third otherwise-identical draw with zero solar irradiance as the ambient/material baseline.
        EnsureContextValid();
        double lit = RenderForwardReceiver(family, 1f);
        double occluded = RenderForwardReceiver(family, 0f);
        double sunOff = RenderForwardReceiver(family, 1f, Vector3.Zero);

        // Assert: The far page contributes direct sun; an occluding page restores the same unshadowed baseline.
        // The 0.005 radiance tolerance covers a few half-float ULPs in the OIT attachment while remaining far below
        // the observed direct-sun contribution; it also detects accidental attenuation of ambient/material light.
        const double radianceTolerance = 0.005;
        Assert.True(lit - sunOff > radianceTolerance,
            $"Expected positive forward direct-sun contribution above the sun-off baseline: lit={lit:R}, sunOff={sunOff:R}.");
        Assert.True(sunOff > radianceTolerance,
            $"Expected a visible ambient/material baseline so the occlusion comparison can detect lost non-solar light: sunOff={sunOff:R}.");
        Assert.True(Math.Abs(occluded - sunOff) <= radianceTolerance,
            $"Expected the occluding VSM page to remove only direct sun and preserve the sun-off ambient/material baseline: occluded={occluded:R}, sunOff={sunOff:R}.");
    }
    #endregion

    #region Private
    /// <summary>Prepares both GPU observation programs used by this isolated native shader execution.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("statistics", 0, 0);
    }

    /// <summary>Compiles the production-patched installed fragment and submits it with explicit current VSM resources.</summary>
    private double RenderForwardReceiver(string family, float pageDepth, Vector3? solar = null)
    {
        bool previousMode = ConfigModSystem.Config.LumOn.Enabled;
        bool? previousGeneration = PbrShaderLightingMode.GenerationLumOnEnabled;
        ConfigModSystem.Config.LumOn.Enabled = true;
        PbrShaderLightingMode.GenerationLumOnEnabled = true;
        try
        {
            using var shadows = new PublishedSurfaceShadowFixture(pageDepth, solar: solar);
            using var framework = new ShaderTestFramework();
            using var shaderPlatform = new EngineShaderPlatformScope();
            using var source = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0.6f, 0.6f, 0.6f, 1f]);
            // Terrain transparency consumes RGB material parameters; a nonzero emission channel verifies that
            // shadow visibility leaves the independent material emission term intact.
            using var materialParams = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0.5f, 0, 0.25f, 0]);
            using var normalDepth = framework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0.5f, 0.5f, 1, 0]);
            using var aerialRadiance = CreateAerialTexture("Tests.NativeForward.AerialRadiance");
            using var aerialAttenuation = CreateAerialTexture("Tests.NativeForward.AerialAttenuation");
            var microfacet = MicrofacetEnergyLookup.Prepare();

            int oit = family == "chunktransparent" ? 1 : 0;
            string vertex = InstalledEngineShaderSources.Build(family + ".vsh", 0, oit, 0, 0, 0);
            string fragment = InstalledEngineShaderSources.Build(family + ".fsh", 0, oit, 0, 0, 0);
            using var modules = new TerrainShaderTestFixture();
            int vertexModule = modules.Compile(ShaderType.VertexShader, vertex);
            int fragmentModule = modules.Compile(ShaderType.FragmentShader, fragment);
            using var executable = TerrainShaderTestFixture.Link(vertexModule, fragmentModule);
            var commands = RHICommandContext.Current;
            var program = new NativeForwardShaderProgram { PassName = family, ProgramId = executable.ProgramId };
            try
            {
                var layout = GpuProgramLayout.TryBuild(executable.ProgramId);
                if (family == "entityanimated")
                {
                    // This native block has no explicit source binding; publish the same owner-selected slot used by the former RHI contract.
                    layout.RegisterUniformBlockBinding("Animation", 1);
                    layout.ApplyContract(executable.ProgramId);
                }
                program.PublishUniformLocations(layout);
                SurfaceShadowPrograms.Register(program);
                Assert.True(SurfaceShadowPrograms.Has(program));
                using var target = framework.CreateTestGBuffer(1, 1,
                    TextureFormat.Rgba32f, TextureFormat.Rgba16f, TextureFormat.Rgba16f, TextureFormat.Rgba16f,
                    TextureFormat.Rgba16f, TextureFormat.Rgba16f, TextureFormat.Rgba32ui, TextureFormat.Rgba16f);
                var colors = Enumerable.Range(0, 8).Select(index => new RHIColorAttachment(
                    AttachmentIndex: index, Load: AttachmentLoad.Clear,
                    Clear: index == 6 ? ColorClearValue.UInt(0, 0, 0, 0) : ColorClearValue.Float(0, 0, 0, 0))).ToArray();
                var pass = FramebufferTestSetup.DescribeAttachments(target, colors);
                using var stateShader = BuiltShaderFixture.LoadFixtureProgram(
                    "tests/complete-state.vsh", "tests/pbr-forward-surface.fsh");
                using var pipeline = commands.CreateGraphicsPipeline(new(stateShader, new([]), commands.DescribeRenderTargets(pass),
                    DynamicPipelineState.Viewport, label: $"Tests.NativeForward.{family}.Vsm")).RequireResource();
                using var engineMesh = NativeForwardEngineMesh.Create(family);
                using var frame = CreateFrameSnapshot();
                using var lights = new VgeLightsUniformBuffer();
                lights.Capture(0, [], []);
                using var bindingOwners = new GpuResourceCollection();
                var frameLighting = bindingOwners.Own(CreateFrameLightingBuffer());
                using var animation = family == "entityanimated" ? CreateAnimationBuffer(commands) : null;
                // Act: Run the installed native owner draw after publishing the same RHI resources used by production consumers.
                bool entered = commands.TryEnterBoundary(new($"Tests.NativeForward.{family}.Vsm", true,
                    graphics: [pipeline], operations: BoundaryOperations.RenderPasses,
                    sampledSlots: NativeSampledSlots(family), uniformSlots: [GpuBindingRegistry.Ubo.Frame,
                        GpuBindingRegistry.Ubo.Lights, 1, 18]), out var boundary);
                Assert.True(entered, OpenGlStateCache.Current.BoundaryEntryFailure?.ToString());
                using (boundary!)
                {
                    boundary!.Run(() =>
                    {
                        commands.BeginRenderPass(pass);
                        commands.SetPipeline(pipeline);
                        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                        BindNativeInputs(commands, family, source, materialParams, normalDepth, aerialRadiance,
                            aerialAttenuation, microfacet, frame, lights, frameLighting, animation);
                        SurfaceShadowBindings.BindNative(shadows.Inputs);
                        SetNativeInputs(program, family);
                        engineMesh.Draw();
                        commands.EndRenderPass();
                        ShaderProgramBase.CurrentShaderProgram = null;
                    });
                }
                foreach (int slot in new[] { GpuBindingRegistry.Ssbo.FrameLighting, 0, 1, 2, 5 })
                    GpuShaderStorageBuffer.UnbindBase(slot);

                // Assert: Reduce the native shaded output on the GPU; OIT terrain stores lit accumulation at attachment 3.
                int output = family == "chunktransparent" ? 3 : 0;
                return ImageAssertions.Statistics.Statistics(target[output], channelMask: 1).Sum(0);
            }
            finally
            {
                SurfaceShadowPrograms.Remove(program);
            }
        }
        finally
        {
            ConfigModSystem.Config.LumOn.Enabled = previousMode;
            PbrShaderLightingMode.GenerationLumOnEnabled = previousGeneration;
        }
    }

    /// <summary>Binds native shader inputs through the RHI's context-owned resource operations.</summary>
    private static void BindNativeInputs(RHICommandContext commands, string family, GpuTextureRef source,
        GpuTextureRef materialParams, GpuTextureRef normalDepth, Texture3D aerialRadiance,
        Texture3D aerialAttenuation, GpuTextureRef microfacet,
        VgeFrameUniformBuffer frame, VgeLightsUniformBuffer lights, GpuShaderStorageBuffer frameLighting,
        GpuUniformBuffer? animation)
    {
        Assert.True(commands.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, frame.Capture()));
        Assert.True(commands.BindUniformSnapshot(GpuBindingRegistry.Ubo.Lights, lights.Capture()));
        frameLighting.BindBase(GpuBindingRegistry.Ssbo.FrameLighting);
        animation?.BindBase(1);
        var sampled = new List<RHISampledTextureSlot>
        {
            new(0, source, GpuSamplers.NearestClamp, TextureDimension.Two),
            new(11, aerialRadiance, GpuSamplers.NearestClamp, TextureDimension.Three),
            new(12, aerialAttenuation, GpuSamplers.NearestClamp, TextureDimension.Three),
            new(32, microfacet, GpuSamplers.LinearClamp, TextureDimension.Two)
        };
        if (family == "chunktransparent")
        {
            sampled.Add(new(34, materialParams, GpuSamplers.NearestClamp, TextureDimension.Two));
            sampled.Add(new(35, normalDepth, GpuSamplers.NearestClamp, TextureDimension.Two));
        }
        commands.BindSampledTextures(new(sampled.ToArray()));
    }

    /// <summary>Creates the documented sampled-unit footprint for the installed native material family.</summary>
    private static RHISampledTextureSlot[] NativeSampledSlots(string family)
    {
        var slots = new List<RHISampledTextureSlot>
        {
            new(0, default, Dimension: TextureDimension.Two),
            new(11, default, Dimension: TextureDimension.Three),
            new(12, default, Dimension: TextureDimension.Three),
            new(32, default, Dimension: TextureDimension.Two),
            new(33, default, Dimension: TextureDimension.TwoArray)
        };
        if (family == "chunktransparent")
        {
            slots.Add(new(34, default, Dimension: TextureDimension.Two));
            slots.Add(new(35, default, Dimension: TextureDimension.Two));
        }
        return slots.ToArray();
    }

    /// <summary>Publishes the installed engine's native vertex arrays and custom interleaves through its mesh owner.</summary>
    private sealed class NativeForwardEngineMesh : IDisposable
    {
        private readonly ClientPlatformWindows render;
        private readonly MeshRef mesh;
        private readonly bool instanced;

        /// <summary>Retains the native engine platform and its corresponding mesh handle.</summary>
        private NativeForwardEngineMesh(ClientPlatformWindows render, MeshRef mesh, bool instanced)
        {
            this.render = render;
            this.mesh = mesh;
            this.instanced = instanced;
        }

        /// <summary>Uploads one full-screen receiver using the installed engine's ordinary vertex contract.</summary>
        internal static NativeForwardEngineMesh Create(string family)
        {
            if (ScreenManager.Platform is not ClientPlatformWindows render)
                throw new InvalidOperationException("The engine rendering platform is unavailable.");
            var data = new MeshData(3);
            try
            {
                data.xyz = [-1, -1, -1, 3, -1, -1, -1, 3, -1];
                data.Uv = [.5f, .5f, .5f, .5f, .5f, .5f];
                data.Rgba = Enumerable.Repeat((byte)255, 12).ToArray();
                data.Flags = [7 << 22, 7 << 22, 7 << 22];
                data.Indices = [0, 1, 2];
                data.SetVerticesCount(3);
                data.SetIndicesCount(3);
                data.SetMode(EnumDrawMode.Triangles);
                AddFamilyAttributes(data, family);
                return new(render, render.UploadMesh(data), family == "instanced");
            }
            finally
            {
                data.Dispose();
            }
        }

        /// <summary>Submits the native mesh through the engine API, including its instance draw path where required.</summary>
        internal void Draw()
        {
            if (instanced) render.RenderMeshInstanced(mesh, 1);
            else render.RenderMesh(mesh);
        }

        /// <summary>Retires the uploaded native mesh through its matching engine owner.</summary>
        public void Dispose() => render.DeleteMesh(mesh);

        /// <summary>Adds only the family-specific fields declared by the installed vertex shader.</summary>
        private static void AddFamilyAttributes(MeshData data, string family)
        {
            if (family == "entityanimated")
            {
                var damage = new CustomMeshDataPartFloat { InterleaveSizes = [1], InterleaveOffsets = [0], InterleaveStride = 4 };
                damage.Add(0f); damage.Add(0f); damage.Add(0f);
                var joints = new CustomMeshDataPartInt { InterleaveSizes = [1], InterleaveOffsets = [0], InterleaveStride = 4 };
                joints.Add(0); joints.Add(0); joints.Add(0);
                data.CustomFloats = damage;
                data.CustomInts = joints;
            }
            else if (family == "instanced")
            {
                var instance = new CustomMeshDataPartFloat
                {
                    InterleaveSizes = [4, 4, 4, 4, 4],
                    InterleaveOffsets = [0, 16, 32, 48, 64],
                    InterleaveStride = 80,
                    Instanced = true
                };
                float[] values = new float[20];
                values[0] = values[1] = values[2] = values[3] = 1;
                values[4] = values[9] = values[14] = values[19] = 1;
                values[16] = 64; values[17] = 64;
                instance.Add(values);
                data.CustomFloats = instance;
            }
            else if (family == "chunktransparent")
            {
                var colormap = new CustomMeshDataPartInt { InterleaveSizes = [1], InterleaveOffsets = [0], InterleaveStride = 4 };
                colormap.Add(-1); colormap.Add(-1); colormap.Add(-1);
                data.CustomInts = colormap;
            }
        }
    }

    /// <summary>Provides one identity transform for every installed animation matrix slot used by the test mesh.</summary>
    private static GpuUniformBuffer CreateAnimationBuffer(RHICommandContext commands)
    {
        float[] values = new float[46 * 16];
        for (int matrix = 0; matrix < 46; matrix++)
        {
            int offset = matrix * 16;
            values[offset] = values[offset + 5] = values[offset + 10] = values[offset + 15] = 1;
        }
        var buffer = commands.CreateUniformBuffer(new RHIBufferDescription
        {
            Role = BufferRole.Uniform, Mode = ResourceStorageMode.Mutable, Capacity = values.Length * sizeof(float),
            Usage = BufferUsage.StaticDraw, Label = "Tests.NativeForward.Animation"
        }).RequireResource();
        buffer.UploadData(MemoryMarshal.AsBytes(values.AsSpan()));
        return buffer;
    }

    /// <summary>Builds a complete frame UBO placing the test camera exactly 64 blocks from the published page origin.</summary>
    private static VgeFrameUniformBuffer CreateFrameSnapshot()
    {
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] view = (float[])identity.Clone();
        view[12] = -64;
        view[13] = -64;
        float[] inverseView = (float[])identity.Clone();
        inverseView[12] = 64;
        inverseView[13] = 64;
        var frame = new VgeFrameUniformBuffer();
        frame.Capture(identity, view, identity, inverseView, identity, identity,
            new(1, 1), 0, 1, new(64, 64, 0), Vector3.Zero, 0);
        return frame;
    }

    /// <summary>Creates valid unit physical-light storage for the native scene-output branch.</summary>
    private static GpuShaderStorageBuffer CreateFrameLightingBuffer()
    {
        var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
        {
            Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 32,
            Usage = BufferUsage.DynamicDraw, Label = "Tests.NativeForward.FrameLighting"
        }).RequireResource();
        byte[] bytes = new byte[32];
        Span<float> values = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        values[0] = 1;
        values[1] = 1;
        values[2] = 16000;
        values[3] = 1;
        buffer.UploadData(bytes);
        return buffer;
    }

    /// <summary>Publishes one neutral 1x1 aerial volume for the compatibility branch and dormant sampler path.</summary>
    private static Texture3D CreateAerialTexture(string label) => RHICommandContext.Current.CreateTexture3D(new RHITextureDescription
    {
        Dimension = TextureDimension.Three, Format = TextureFormat.Rgba16f, Width = 1, Height = 1, Depth = 1,
        MipLevels = 1, Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest,
        BaseLevel = 0, MaxLevel = 0, Label = label
    }).RequireResource();

    /// <summary>Assigns controlled native standalone inputs without replacing the production fragment source.</summary>
    private static void SetNativeInputs(NativeForwardShaderProgram program, string family)
    {
        float[] identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] view = (float[])identity.Clone();
        view[12] = -64;
        view[13] = -64;
        float[] model = (float[])identity.Clone();
        model[12] = 64;
        model[13] = 64;
        program.Use();
        program.UniformMatrix("projectionMatrix", identity);
        if (family == "instanced") program.UniformMatrix("modelViewMatrix", view);
        else
        {
            program.UniformMatrix("viewMatrix", view);
            if (family == "standard" || family == "entityanimated") program.UniformMatrix("modelMatrix", model);
        }
        program.Uniform("vge_pbrRoute", 2);
        program.Uniform("vge_atmosphereAerialParams", new Vec4f(0, 0, 1, 0));
        program.Uniform("vge_atmosphereEnvironment", new Vec3f(0, 0, 0));
        program.Uniform("vge_atmosphereScatteringSun", new Vec3f(0, 0, 0));
        program.Uniform("vge_atmosphereScatteringMoon", new Vec3f(0, 0, 0));
        program.Uniform(family switch { "entityanimated" => "entityTex", "chunktransparent" => "terrainTex", _ => "tex" }, 0);
        program.Uniform("vge_atmosphereAerialRadiance", 11);
        program.Uniform("vge_atmosphereAerialAttenuation", 12);
        program.Uniform("vge_microfacetEnergy", 32);
        program.Uniform("shadowDepthPool", 33);
        if (family == "chunktransparent")
        {
            program.Uniform("vge_materialParamsTex", 34);
            program.Uniform("vge_normalDepthTex", 35);
        }
        program.Uniform("rgbaTint", new Vec4f(1, 1, 1, 1));
        program.Uniform("renderColor", new Vec4f(1, 1, 1, 1));
        // Standard and animated-entity families receive controlled nonsolar block light through this native draw input.
        // Instanced and chunk-transparent families retain their own authored block-light vertex/instance data.
        Vec4f blockLight = family is "standard" or "entityanimated"
            ? new Vec4f(0.25f, 0.25f, 0.25f, 1)
            : new Vec4f(0, 0, 0, 1);
        program.Uniform("rgbaLightIn", blockLight);
        program.Uniform("rgbaAmbientIn", new Vec3f(1, 1, 1));
        program.Uniform("rgbaGlowIn", new Vec4f(0, 0, 0, 0));
        program.Uniform("rgbaFogIn", new Vec4f(0, 0, 0, 0));
        program.Uniform("extraGlow", 0);
        program.Uniform("fogMinIn", 0f);
        program.Uniform("fogDensityIn", 0f);
        program.Uniform("dontWarpVertices", 1);
        program.Uniform("fadeFromSpheresFog", 0);
        program.Uniform("addRenderFlags", 0);
        program.Uniform("extraZOffset", 0f);
        program.Uniform("viewDistance", 256f);
        program.Uniform("flatFogDensity", 0f);
        program.Uniform("flatFogStart", 0f);
        program.Uniform("viewDistanceLod0", 256f);
        program.Uniform("glitchStrengthFL", 0f);
        program.Uniform("nightVisionStrength", 0f);
        program.Uniform("origin", new Vec3f(64, 64, 0));
        program.UniformMatrix("modelViewMatrix", view);
        program.Uniform("forcedTransparency", 0f);
        program.Uniform("subpixelPaddingX", 0f);
        program.Uniform("subpixelPaddingY", 0f);
        program.Uniform("playerpos", new Vec3f(64, 64, 0));
        program.Uniform("timeCounter", 0f);
        program.Uniform("windWaveCounter", 0f);
        program.Uniform("windWaveCounterHighFreq", 0f);
        program.Uniform("waterWaveCounter", 0f);
        program.Uniform("windSpeed", 0f);
        program.Uniform("globalWarpIntensity", 0f);
        program.Uniform("seasonRel", 0f);
        program.Uniform("seaLevel", 0f);
        program.Uniform("atlasHeight", 1f);
        program.Uniform("seasonTemperature", 0f);
        program.Uniform("skipRenderJointId", -1);
        program.Uniform("skipRenderJointId2", -1);
        program.Uniform("frostAlpha", 0f);
        program.Uniform("depthOffset", 0f);
        program.Uniform("alphaTest", 0f);
        program.Uniform("glitchEffectStrength", 0f);
        program.Uniform("glitchFlicker", 0);
        program.Uniform("entityId", 0);
    }
    #endregion

    #region Test fixture types
    /// <summary>Publishes installed native uniform locations using the shared reflected program snapshot.</summary>
    private sealed class NativeForwardShaderProgram : ShaderProgram
    {
        /// <summary>Publishes locations acquired from the linked installed shader for the native owner setters.</summary>
        internal void PublishUniformLocations(GpuProgramLayout layout)
        {
            foreach (string name in new[]
            {
                "tex", "entityTex", "terrainTex", "vge_atmosphereAerialRadiance", "vge_atmosphereAerialAttenuation",
                "vge_microfacetEnergy", "shadowDepthPool", "vge_materialParamsTex", "vge_normalDepthTex",
                "projectionMatrix", "viewMatrix", "modelMatrix", "modelViewMatrix", "origin", "vge_pbrRoute", "vge_atmosphereAerialParams",
                "vge_atmosphereEnvironment", "vge_atmosphereScatteringSun", "vge_atmosphereScatteringMoon", "rgbaTint",
                "rgbaLightIn", "rgbaAmbientIn", "rgbaGlowIn", "rgbaFogIn", "extraGlow", "fogMinIn", "fogDensityIn",
                "dontWarpVertices", "fadeFromSpheresFog", "addRenderFlags", "extraZOffset", "viewDistance",
                "flatFogDensity", "flatFogStart", "viewDistanceLod0", "glitchStrengthFL", "nightVisionStrength", "renderColor",
                "forcedTransparency", "subpixelPaddingX", "subpixelPaddingY", "playerpos", "timeCounter", "windWaveCounter",
                "windWaveCounterHighFreq", "waterWaveCounter", "windSpeed", "globalWarpIntensity", "seasonRel", "seaLevel",
                "atlasHeight", "seasonTemperature", "skipRenderJointId", "skipRenderJointId2", "frostAlpha", "depthOffset",
                "alphaTest", "glitchEffectStrength", "glitchFlicker", "entityId"
            })
                uniformLocations[name] = layout.GetUniformLocation(ProgramId, name);
        }
    }
    #endregion
}
