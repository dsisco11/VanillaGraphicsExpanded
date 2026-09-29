using System.Reflection;
using System.Numerics;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises atmospheric sampler discovery through installed engine compilation and use.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereEngineBindingTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Engine lifecycle
    /// <summary>Checks the real installed sky receives the published direction through engine use.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SkyUseBindsPublishedMieDirection(bool raster)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        typeof(ClientPlatformWindows).GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(Vintagestory.Client.ScreenManager.Platform, new InertLogger());
        var harmony = new Harmony("VGE.Tests.AtmosphereSkyBindings");
        using var owner = new AtmosphereModSystem();
        var lookup = new AtmosphereLookup();
        var sun = Vector3.Normalize(new Vector3(.3f, .8f, .4f));
        lookup.Update(sun, .1f, 0, complete: true, width: 16, height: 8);
        owner.Publish(lookup.Current!);
        var program = new ShaderProgram
        {
            AssetDomain = "game", PassName = "sky",
            VertexShader = Stage("sky.vsh", EnumShaderType.VertexShader, 0),
            FragmentShader = Stage("sky.fsh", EnumShaderType.FragmentShader, 0)
        };
        if (raster)
        {
            // Preserve the installed fragment and all engine binding ownership while
            // controlling only the viewing direction supplied by sky dome geometry.
            program.VertexShader.Code = """
                #version 330 core
                uniform vec3 sampleDirection;
                out vec3 vertexPosition;
                out vec4 rgbaFog;
                out float nightVisionStrengthv;
                void main() {
                    vec2 corners[3] = vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
                    gl_Position = vec4(corners[gl_VertexID],0,1);
                    vertexPosition = sampleDirection;
                    rgbaFog = vec4(0);
                    nightVisionStrengthv = 0;
                }
                """;
        }
        try
        {
            harmony.CreateClassProcessor(typeof(AtmosphereShaderCompilationHook)).Patch();
            harmony.CreateClassProcessor(typeof(AtmosphereShaderBindingHook)).Patch();
            Assert.True(program.Compile());
            program.Use();
            int location = GL.GetUniformLocation(program.ProgramId, "vge_atmosphereSunDirection");
            Assert.True(location >= 0);
            float[] actual = new float[3];
            GL.GetUniform(program.ProgramId, location, actual);
            Assert.InRange(MathF.Abs(actual[0] - sun.X), 0, .00001f);
            Assert.InRange(MathF.Abs(actual[1] - sun.Y), 0, .00001f);
            Assert.InRange(MathF.Abs(actual[2] - sun.Z), 0, .00001f);
            GL.GetUniform(program.ProgramId, GL.GetUniformLocation(program.ProgramId, "vge_atmosphereSky"), out int unit);
            Assert.Equal(13, unit);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            if (raster)
            {
                using var vao = GpuVao.Create();
                using var framework = new ShaderTestFramework();
                using var target = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba32f);
                using var dryDepth = framework.CreateTexture(1, 1, PixelInternalFormat.R32f, [1f]);
                GlStateCache.Current.BindVertexArray(vao.VertexArrayId);
                GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
                float[] brightness = new float[6];
                var lighting = lookup.Current!;
                // Compare the split representation to both the preceding total-radiance
                // interpolation and a background-only control, not just two sky directions.
                float[] background = lighting.Sky.ToArray();
                for (int y = 0; y < lighting.Height; y++)
                for (int x = 0; x < lighting.Width; x++)
                {
                    float factor = AtmosphereMieTransport.Factor(Vector3.Dot(
                        AtmosphereMieTransport.Direction(x, y, lighting.Width, lighting.Height, lighting.HorizonElevation), sun));
                    int offset = (y * lighting.Width + x) * 4;
                    for (int channel = 0; channel < 3; channel++)
                        background[offset + channel] = MathF.Max(0, background[offset + channel] - lighting.SkyMie[offset + channel] * factor);
                }
                for (int mode = 0; mode < 3; mode++)
                {
                    owner.Publish(mode switch
                    {
                        0 => lighting,
                        1 => lighting with { SkyMie = ImmutableArray<float>.Empty },
                        _ => lighting with { Sky = ImmutableArray.CreateRange(background), SkyMie = ImmutableArray<float>.Empty }
                    });
                    for (int i = 0; i < 2; i++)
                    {
                        var direction = i == 0 ? sun : Vector3.Normalize(sun
                            + Vector3.Normalize(Vector3.Cross(sun, Vector3.UnitY)) * MathF.Tan(MathF.PI / 6));
                        target.BindWithViewport();
                        program.Stop();
                        program.Use();
                        program.Uniform("sampleDirection", direction.X, direction.Y, direction.Z);
                        program.Uniform("dayLight", 1f);
                        // Preserve installed underwater handling with an actual dry-scene depth input.
                        program.BindTexture2D("liquidDepth", dryDepth.TextureId, 0);
                        program.Uniform("frameSize", 1f, 1f);
                        program.Uniform("cameraUnderwater", 0f);
                        program.Uniform("zNear", .5f);
                        program.Uniform("zFar", 1024f);
                        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
                        Assert.Equal(ErrorCode.NoError, GL.GetError());
                        float[] color = target[0].ReadPixels();
                        brightness[mode * 2 + i] = color[0] + color[1] + color[2];
                        output.WriteLine($"Installed sky mode {mode} at {i * 30} degrees: ({color[0]}, {color[1]}, {color[2]}, {color[3]})");
                    }
                }
                Assert.True(brightness[0] > brightness[1], $"Sun={brightness[0]}, 30deg={brightness[1]}");
                Assert.True(brightness[0] > brightness[4], $"Split={brightness[0]}, background={brightness[4]}");
                Assert.True(brightness[1] > brightness[5], $"Split30={brightness[1]}, background30={brightness[5]}");
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            program.Stop();
            AtmosphereProgramBindings.Remove(program);
            program.Dispose();
        }
    }

    /// <summary>First-person standard programs assign active volume samplers without caller initialization.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void StandardCompileAndUseAssignsAerialSamplers(int ssao)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        typeof(ClientPlatformWindows).GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .SetValue(Vintagestory.Client.ScreenManager.Platform, new InertLogger());
        var harmony = new Harmony("VGE.Tests.AtmosphereEngineBindings");
        var program = new ShaderProgram
        {
            AssetDomain = "game", PassName = "standard",
            VertexShader = Stage("standard.vsh", EnumShaderType.VertexShader, ssao),
            FragmentShader = Stage("standard.fsh", EnumShaderType.FragmentShader, ssao)
        };
        try
        {
            harmony.CreateClassProcessor(typeof(AtmosphereShaderCompilationHook)).Patch();
            harmony.CreateClassProcessor(typeof(AtmosphereShaderBindingHook)).Patch();
            Assert.True(program.Compile());
            Assert.True(GL.GetError() == ErrorCode.NoError, "Error during engine compilation");
            AssertSamplerUnits(program);
            Assert.True(GL.GetError() == ErrorCode.NoError, "Error during initial sampler inspection");
            program.Use();
            Assert.True(GL.GetError() == ErrorCode.NoError, "Error during engine Use");
            AssertSamplerUnits(program);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            program.Stop();
            AtmosphereProgramBindings.Remove(program);
            program.Dispose();
        }
    }

    /// <summary>Checks driver state and the repaired engine cache without supplying sampler assignments.</summary>
    private static void AssertSamplerUnits(ShaderProgram program)
    {
        foreach (var (name, expected) in new[]
        {
            ("vge_atmosphereAerialRadiance", 11),
            ("vge_atmosphereAerialAttenuation", 12)
        })
        {
            Assert.True(program.HasUniform(name), name);
            int location = GL.GetUniformLocation(program.ProgramId, name);
            Assert.True(location >= 0, name);
            GL.GetUniform(program.ProgramId, location, out int unit);
            Assert.Equal(expected, unit);
        }
    }
    #endregion

    /// <summary>Supplies engine compiler logging without opening files.</summary>
    private sealed class InertLogger : Vintagestory.Logger
    {
        /// <summary>Disables file creation through the overridden log path.</summary>
        internal InertLogger() : base("atmosphere-test", false, 0, 0) { }
        /// <summary>No destination is used by the isolated compiler.</summary>
        public override string getLogFile(Vintagestory.API.Common.EnumLogType type) => null!;
        /// <summary>Compiler diagnostics remain visible when a test fails.</summary>
        protected override void LogImpl(Vintagestory.API.Common.EnumLogType type, string format, params object[] args) => Console.WriteLine(format, args);
        /// <summary>Logging is handled directly by the test sink.</summary>
        public override bool printToConsole(Vintagestory.API.Common.EnumLogType type) => false;
        /// <summary>Does not emit debugger traffic.</summary>
        public override bool printToDebugWindow(Vintagestory.API.Common.EnumLogType type) => false;
    }

    #region Installed assets
    /// <summary>Uses the existing production patch expansion and lets the engine own shader compilation.</summary>
    private static Shader Stage(string name, EnumShaderType type, int ssao)
    {
        var stage = new Shader
        {
            shaderType = type, PrefixCode = "",
            Code = PbrSurfaceInstalledShaderTests.Build(name, 0, 0, ssao, 0, 1)
        };
        typeof(Shader).GetField("Filename", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(stage, name);
        return stage;
    }
    #endregion
}
