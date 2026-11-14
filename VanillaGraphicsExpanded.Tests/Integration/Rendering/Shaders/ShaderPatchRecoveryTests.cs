using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;


using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Exercises bounded patch rollback through the installed engine compiler.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderPatchRecoveryTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared driver context without starting a game.</summary>
    public ShaderPatchRecoveryTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Recovery
    /// <summary>Failed patched compilation restores every source, retains late prefixes and leaves other programs intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EngineCompileRecoversOriginalProgramOnly(bool linkFailure)
    {
        // Arrange: Compile a peer program and save original owner source before introducing compile or link failure.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var harmony = new Harmony("VGE.Tests.ShaderPatchRecovery");
        var report = EngineShaderProcessingHook.ReportError;
        var messages = new List<string>();
        EngineShaderProcessingHook.ReportError = (_, text) => messages.Add(text);
        var owner = CreateProgram();
        var unrelated = CreateProgram();
        try
        {
            harmony.CreateClassProcessor(typeof(ShaderPatchCompilationHook)).Patch();
            Assert.True(unrelated.Compile());
            int other = unrelated.ProgramId;
            string vertex = owner.VertexShader.Code, fragment = owner.FragmentShader.Code;
            ShaderPatchRecovery.Capture(owner);
            ShaderCapabilities.Declare(owner, ShaderCapability.TwoSidedSurfaceNormals);
            ShaderCapabilities.Declare(owner, ShaderCapability.SharedVertexDeformation);
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
            // Act: Compile the broken patched owner and let the installed recovery hook restore its original sources.
            // Assert: Recovery withdraws the patch capability, restores original source, retains the late prefix, and leaves the peer executable intact.
            Assert.True(owner.Compile());
            Assert.False(ShaderCapabilities.Has(owner, ShaderCapability.TwoSidedSurfaceNormals));
            Assert.False(ShaderCapabilities.Has(owner, ShaderCapability.SharedVertexDeformation));
            Assert.Equal(vertex, owner.VertexShader.Code);
            Assert.Equal(fragment, owner.FragmentShader.Code);
            Assert.Equal("#define LATE_ENGINE_VALUE 1\n", owner.VertexShader.PrefixCode);
            // These driver name queries and deletion observations verify engine shader lifetime beyond RHI bookkeeping.
            Assert.True(GL.IsProgram(owner.ProgramId));
            Assert.Equal(other, unrelated.ProgramId);
            Assert.True(GL.IsProgram(other));
            Assert.False(ShaderPatchRecovery.TryRecover(owner, () => throw new InvalidOperationException("retry consumed"), _ => { }, out _));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            EngineShaderProcessingHook.ReportError = report;
            owner.Dispose();
            unrelated.Dispose();
            ShaderPatchRecovery.Forget(owner);
        }
    }

    /// <summary>A broken original shader returns failure once and cannot enter recursive retries.</summary>
    [Fact]
    public void BrokenOriginalStopsAfterSingleRetry()
    {
        // Arrange: Keep the original fragment invalid, then add a separate invalid patch to trigger recovery.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var owner = CreateProgram();
        var messages = new List<string>();
        var glErrors = new List<ErrorCode>();
        try
        {
            owner.FragmentShader.Code += "\ninvalid original syntax";
            ShaderPatchRecovery.Capture(owner);
            owner.VertexShader.Code += "\ninvalid patch syntax";
            // Act: Compile the patch and invoke recovery once against the still-broken original.
            // Assert: Recovery reports failure after exactly one retry and does not recurse on later attempts.
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
        finally
        {
            try { DisposeAfterRecoveryAttempt(owner); }
            finally
            {
                try { CollectOpenGlErrors(glErrors); }
                finally { ShaderPatchRecovery.Forget(owner); }
            }
        }

        Assert.Empty(glErrors);
    }

    /// <summary>A recovery retry compiles the restored source only after its authoritative preparation callback edits it.</summary>
    [Fact]
    public void RecoveryPreparesRestoredSourceBeforeItsSingleCompile()
    {
        // Arrange: Retain a valid baseline, then force one failed engine compile with a broken candidate.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var owner = CreateProgram();
        var messages = new List<string>();
        string originalVertex = owner.VertexShader.Code;
        int preparations = 0;
        int compileAttempts = 0;
        try
        {
            ShaderPatchRecovery.Capture(owner);
            owner.VertexShader.Code += "\ninvalid candidate syntax";
            Assert.False(owner.Compile());

            // Act: Preparation must receive the restored baseline and publish an edit before the only retry.
            bool handled = ShaderPatchRecovery.TryRecover(owner, () =>
            {
                compileAttempts++;
                return owner.Compile();
            }, messages.Add, out bool recovered, program =>
            {
                preparations++;
                Assert.Equal(originalVertex, program.VertexShader.Code);
                program.VertexShader.Code = originalVertex.Replace("#version 330 core", "#version 330 core\n#define VGE_RECOVERY_PREPARED 1", StringComparison.Ordinal);
            });

            // Assert: The prepared source is the one that linked, and the bounded recovery callback ran once.
            Assert.True(handled);
            Assert.True(recovered);
            Assert.Equal(1, preparations);
            Assert.Equal(1, compileAttempts);
            Assert.Contains("#define VGE_RECOVERY_PREPARED 1", owner.VertexShader.Code, StringComparison.Ordinal);
            Assert.Single(messages);
            Assert.False(ShaderPatchRecovery.TryRecover(owner, () => throw new InvalidOperationException("retry consumed"), _ => { }, out _));
        }
        finally
        {
            try { DisposeAfterRecoveryAttempt(owner); }
            finally { ShaderPatchRecovery.Forget(owner); }
        }
    }

    /// <summary>A preparation failure consumes recovery without attempting compilation or permitting another recovery.</summary>
    [Fact]
    public void PreparationFailureConsumesRecoveryBeforeCompile()
    {
        // Arrange: Retain a valid baseline and trigger a failed compile after corrupting its candidate source.
        EnsureContextValid();
        using var platform = new EngineShaderPlatformScope();
        InstallLogger();
        var owner = CreateProgram();
        var messages = new List<string>();
        int compileAttempts = 0;
        int preparationAttempts = 0;
        try
        {
            ShaderPatchRecovery.Capture(owner);
            owner.VertexShader.Code += "\ninvalid candidate syntax";
            Assert.False(owner.Compile());

            // Act: Fail during restored-source preparation before allowing the compiler callback to run.
            Assert.Throws<InvalidOperationException>(() => ShaderPatchRecovery.TryRecover(owner, () =>
            {
                compileAttempts++;
                return owner.Compile();
            }, messages.Add, out _, _ =>
            {
                preparationAttempts++;
                throw new InvalidOperationException("preparation failed");
            }));

            // Assert: The failed preparation used the sole recovery opportunity and never reached compilation.
            Assert.Equal(1, preparationAttempts);
            Assert.Equal(0, compileAttempts);
            Assert.False(ShaderPatchRecovery.TryRecover(owner, () => throw new InvalidOperationException("retry consumed"), _ => { }, out _));
        }
        finally
        {
            try { DisposeAfterRecoveryAttempt(owner); }
            finally { ShaderPatchRecovery.Forget(owner); }
        }
    }
    #endregion
    #endregion

    #region Private
    #region Cleanup
    /// <summary>Releases partial shader objects without asking the engine to detach them from program zero.</summary>
    private static void DisposeAfterRecoveryAttempt(ShaderProgram program)
    {
        if (program.ProgramId != 0)
        {
            program.Dispose();
            return;
        }

        // A failed recovery has no linked program, but the engine can leave compiled stage handles on the owner.
        foreach (var stage in new[] { program.VertexShader, program.FragmentShader, program.GeometryShader })
        {
            if (stage is null || stage.ShaderId == 0) continue;
            GL.DeleteShader(stage.ShaderId);
            stage.ShaderId = 0;
        }

        // ShaderProgram.Dispose detaches every nonnull stage, so remove the emptied stages before disposing the owner.
        program.VertexShader = null!;
        program.FragmentShader = null!;
        program.GeometryShader = null!;
        program.Dispose();
    }

    /// <summary>Collects queued driver errors during cleanup so they cannot leak into a later integration test.</summary>
    private static void CollectOpenGlErrors(List<ErrorCode> errors)
    {
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError) errors.Add(error);
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
        #region Public API
        /// <summary>No log destination is created for this isolated compiler fixture.</summary>
        public override string getLogFile(EnumLogType type) => null!;
        /// <summary>Suppresses console output from the isolated logger.</summary>
        public override bool printToConsole(EnumLogType type) => false;
        /// <summary>Suppresses debugger output from the isolated logger.</summary>
        public override bool printToDebugWindow(EnumLogType type) => false;
        #endregion

        #region Private
        /// <summary>Prevents rotation and opens no files through the overridden path lookup.</summary>
        internal InertLogger() : base("shader-test", false, 0, 0) { }
        /// <summary>The test assertions capture recovery diagnostics separately.</summary>
        protected override void LogImpl(EnumLogType type, string format, params object[] args) { }
        #endregion
    }
    #endregion
    #endregion
}

