using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises bounded patch rollback through the installed engine compiler.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderPatchRecoveryTests : RenderTestBase
{
    /// <summary>Uses the shared driver context without starting a game.</summary>
    public ShaderPatchRecoveryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Recovery
    /// <summary>Failed patched compilation restores every source, retains late prefixes and leaves other programs intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EngineCompileRecoversOriginalProgramOnly(bool linkFailure)
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var harmony = new Harmony("VGE.Tests.ShaderPatchRecovery");
        var report = ShaderIncludesHook.ReportError;
        var messages = new List<string>();
        ShaderIncludesHook.ReportError = (_, text) => messages.Add(text);
        var owner = CreateProgram();
        var unrelated = CreateProgram();
        try
        {
            harmony.CreateClassProcessor(typeof(ShaderPatchCompilationHook)).Patch();
            Assert.True(unrelated.Compile());
            int other = unrelated.ProgramId;
            string vertex = owner.VertexShader.Code, fragment = owner.FragmentShader.Code;
            ShaderPatchRecovery.Capture(owner);
            owner.VertexShader.PrefixCode = "#define LATE_ENGINE_VALUE 1\n";
            if (linkFailure)
            {
                owner.VertexShader.Code = "#version 330 core\nuniform vec3 mismatch;\nvoid main() { gl_Position=vec4(mismatch,1); }";
                owner.FragmentShader.Code = "#version 330 core\nuniform vec4 mismatch;\nout vec4 color;\nvoid main() { color=mismatch; }";
            }
            else
            {
                owner.VertexShader.Code += "\nuniform float failedUniform;\ninvalid patched vertex syntax";
                owner.FragmentShader.Code += "\ninvalid patched fragment syntax";
            }
            Assert.True(owner.Compile());
            Assert.Equal(vertex, owner.VertexShader.Code);
            Assert.Equal(fragment, owner.FragmentShader.Code);
            Assert.Equal("#define LATE_ENGINE_VALUE 1\n", owner.VertexShader.PrefixCode);
            Assert.True(GL.IsProgram(owner.ProgramId));
            Assert.Equal(other, unrelated.ProgramId);
            Assert.True(GL.IsProgram(other));
            Assert.Single(messages, message => message.Contains("restoring the original", StringComparison.Ordinal));
            Assert.False(ShaderPatchRecovery.TryRecover(owner, () => throw new InvalidOperationException("retry consumed"), _ => { }, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            ShaderIncludesHook.ReportError = report;
            owner.Dispose();
            unrelated.Dispose();
            ShaderPatchRecovery.Forget(owner);
        }
    }

    /// <summary>A broken original shader returns failure once and cannot enter recursive retries.</summary>
    [Fact]
    public void BrokenOriginalStopsAfterSingleRetry()
    {
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var owner = CreateProgram();
        var messages = new List<string>();
        try
        {
            owner.FragmentShader.Code += "\ninvalid original syntax";
            ShaderPatchRecovery.Capture(owner);
            owner.VertexShader.Code += "\ninvalid patch syntax";
            Assert.False(owner.Compile());
            int failedStage = owner.VertexShader.ShaderId;
            int attempts = 0;
            Assert.True(ShaderPatchRecovery.TryRecover(owner, () => { attempts++; Assert.False(GL.IsShader(failedStage)); return owner.Compile(); }, messages.Add, out bool recovered));
            Assert.False(recovered);
            Assert.Equal(1, attempts);
            Assert.Equal(2, messages.Count);
            Assert.False(ShaderPatchRecovery.TryRecover(owner, () => { attempts++; return true; }, messages.Add, out _));
            Assert.Equal(1, attempts);
        }
        finally { owner.Dispose(); ShaderPatchRecovery.Forget(owner); }
    }
    #endregion

    #region Engine fixture
    /// <summary>Builds ordinary engine shader stages so production compilation owns source preparation and handles.</summary>
    private static ShaderProgram CreateProgram()
    {
        var program = new ShaderProgram()
        {
            PassName = "recovery-test",
            VertexShader = new Shader
            {
                shaderType = EnumShaderType.VertexShader, PrefixCode = "",
                Code = "#version 330 core\nvoid main() { gl_Position=vec4(0,0,0,1); }"
            },
            FragmentShader = new Shader
            {
                shaderType = EnumShaderType.FragmentShader, PrefixCode = "",
                Code = "#version 330 core\nout vec4 color;\nvoid main() { color=vec4(1); }"
            }
        };
        typeof(Shader).GetField("Filename", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(program.VertexShader, "recovery.vsh");
        typeof(Shader).GetField("Filename", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(program.FragmentShader, "recovery.fsh");
        return program;
    }

    /// <summary>Supplies inert logging because failed engine shader compilation accesses its concrete logger.</summary>
    private static void InstallLogger() => typeof(ClientPlatformWindows).GetField("logger", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
        .SetValue(ScreenManager.Platform, new InertLogger());

    /// <summary>Suppresses engine log files while retaining the real compiler implementation.</summary>
    private sealed class InertLogger : Vintagestory.Logger
    {
        /// <summary>Prevents rotation and opens no files through the overridden path lookup.</summary>
        internal InertLogger() : base("shader-test", false, 0, 0) { }
        /// <summary>No log destination is created for this isolated compiler fixture.</summary>
        public override string getLogFile(EnumLogType type) => null!;
        /// <summary>The test assertions capture recovery diagnostics separately.</summary>
        protected override void LogImpl(EnumLogType type, string format, params object[] args) { }
        /// <summary>Suppresses console output from the isolated logger.</summary>
        public override bool printToConsole(EnumLogType type) => false;
        /// <summary>Suppresses debugger output from the isolated logger.</summary>
        public override bool printToDebugWindow(EnumLogType type) => false;
    }
    #endregion
}

