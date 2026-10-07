using System.Reflection;
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
public sealed class AtmosphereEngineBindingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Engine lifecycle
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
