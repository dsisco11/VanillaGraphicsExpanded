using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks binary-only graphics execution and records driver preparation separately from drawing.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SpirvGraphicsLifecycleTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared GL context and test receipt output.</summary>
    public SpirvGraphicsLifecycleTests(HeadlessGLFixture fixture, ITestOutputHelper output) : base(fixture) => this.output = output;

    #region Binary execution
    /// <summary>Retains active and optimized-out diagnostic locations from compiled contracts without engine storage.</summary>
    [Fact]
    public void BinaryOnlyReloadPreservesContractUniformDictionary()
    {
        // Arrange: Initialize the binary-only graphics loader with the contract's active and optimized-out uniforms.
        // Act: Reload from the built SPIR-V binaries and inspect the resulting contract dictionary.
        // Assert: Both active and optimized-out diagnostic locations remain represented without engine storage.
        EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        assets.BeforeRead = AssertBinaryAsset;
        using var program = new FixtureProgram("lumon_debug_view_direct_total");
        program.Initialize(assets.Api);
        // Action/check: Compile/link each binary generation and compare active plus optimized-out contract locations.
        for (int generation = 0; generation < 2; generation++)
        {
            Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
            var expected = program.ResourceBindings.BinaryInterface!.Uniforms;
            Assert.NotEmpty(expected);
            Assert.Contains(expected, pair => pair.Value == -1);
            Assert.Contains(expected, pair => pair.Value >= 0);
            Assert.Equal(expected.OrderBy(pair => pair.Key), program.DiagnosticUniformLocations.OrderBy(pair => pair.Key));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        Assert.NotEmpty(assets.Reads);
        Assert.All(assets.Reads, AssertBinaryAsset);
    }

    /// <summary>Uses the actual runtime program path to replace binaries and preserve a working generation on binary load failure.</summary>
    [Fact]
    public void RuntimeReloadPreservesWorkingProgramOnInvalidBinary()
    {
        // Arrange: Install a valid runtime program generation and retain its executable/interface identity.
        // Act: Reload using an invalid cached binary, then use the valid runtime path again.
        // Assert: The failed load leaves the working generation installed and a later valid load succeeds.
        EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        var program = new FixtureProgram();
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            int first = program.ProgramId;
            var firstTable = program.ResourceBindings.BinaryInterface!.PreparedBindings;
            // Action/check: Install a replacement generation, then reject corrupt input while retaining that valid generation.
            Assert.True(program.CompileAndLink());
            Assert.NotSame(firstTable, program.ResourceBindings.BinaryInterface!.PreparedBindings);
            Assert.NotEqual(first, program.ProgramId);
            Assert.False(GL.IsProgram(first));
            int current = program.ProgramId;
            var currentTable = program.ResourceBindings.BinaryInterface!.PreparedBindings;
            const string source = "shaders/tests/render_infrastructure.fsh.spv";
            assets.Overrides[source] = new byte[20];
            Assert.False(program.CompileAndLink());
            Assert.Equal(current, program.ProgramId);
            Assert.Same(currentTable, program.ResourceBindings.BinaryInterface!.PreparedBindings);
            Assert.True(GL.IsProgram(current));
            assets.Overrides.Clear();
            Assert.True(program.CompileAndLink());
#if DEBUG
#endif
        }
        finally
        {

            program.Dispose();
        }
    }

    /// <summary>Valid binaries failing specialization or interface linking leave the installed generation intact.</summary>
    [Theory]
    [InlineData("tests/render_infrastructure.fsh.spv", "", "controlled asset read failure")]
    [InlineData("tests/render_infrastructure.fsh.spv", "vge_worldprobe_orbs_points.vsh.spv", "specialization")]
    [InlineData("tests/render_infrastructure.vsh.spv", "vge_worldprobe_orbs_points.vsh.spv", "link")]
    [InlineData("tests/render_infrastructure.fsh.spv", "pbr_display_resolve.fsh.spv", "binding contract")]
    public void RuntimeFailurePreservesInstalledGeneration(string replaced, string substitute, string failure)
    {
        // Arrange: Install a valid executable and configure the selected runtime binary read/specialization failure.
        // Act: Attempt replacement using the row's controlled failure, then restore valid assets and reload.
        // Assert: The failed attempt preserves the installed generation and the repaired input can compile.
        EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        var settings = program.InstalledSettings;
        var layout = program.ResourceBindings;
        var table = layout.BinaryInterface!.PreparedBindings;
        // Action/check: Inject each read/specialization/link failure, preserve the installed generation, then restore assets and retry.
        if (substitute.Length == 0)
            assets.BeforeRead = path => { if (path == "shaders/" + replaced) throw new IOException("controlled asset read failure"); };
        else
        {
            assets.Overrides["shaders/" + replaced] = TestAssetSnapshot.ReadBuiltBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", substitute));
            // Keep the substituted bytes and declarations coherent so this deliberately invalid
            // stage pairing still exercises specialization, linking or binding-contract rejection.
            var manifest = ShaderBinaryDigest.Parse(TestAssetSnapshot.ReadBuiltBytes(Path.Combine(AppContext.BaseDirectory,
                "assets", "shaders", ShaderBinaryDigest.FileName)))!;
            var original = manifest.Binaries[replaced].Interface!;
            var replacement = manifest.Binaries[substitute];
            manifest.Binaries[replaced] = replacement with { Interface = replacement.Interface! with
                { Stage = original.Stage, EntryPoint = original.EntryPoint, StructuralKey = original.StructuralKey } };
            assets.Overrides["shaders/" + ShaderBinaryDigest.FileName] = ShaderBinaryDigest.Encode(manifest.Binaries);
            ShaderDigestIndexCache.Clear();
        }
        Assert.False(program.CompileAndLink());
        Assert.Contains(assets.Logs, message => message.Contains(failure, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(installed, program.ProgramId);
        Assert.Same(settings, program.InstalledSettings);
        Assert.Same(layout, program.ResourceBindings);
        Assert.Same(table, program.ResourceBindings.BinaryInterface!.PreparedBindings);
        Assert.True(GL.IsProgram(installed));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        assets.Overrides.Clear();
        assets.BeforeRead = null;
        ShaderDigestIndexCache.Clear();
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
    }

    /// <summary>Repeated draws retain SPIR-V shader objects and produce the expected interpolated pixel.</summary>
    [Fact]
    public void RepeatedDrawsUseBuiltBinaries()
    {
        // Arrange: Build the packaged graphics binaries and prepare the target and input geometry for repeated draws.
        // Act: Link through the built binaries and submit the same draw repeatedly.
        // Assert: The observed interpolated pixel remains within the expected range on each draw.
        EnsureContextValid();
        using var freshStages = DriverProgramCache.UseStoreForTesting(null);
        GpuShaderModule? vertex = null, fragment = null;
        GpuProgramObject? programOwner = null;
        try
        {
            vertex = BuiltShaderFixture.Load("tests/render_infrastructure.vsh", ShaderType.VertexShader);
            fragment = BuiltShaderFixture.Load("tests/render_infrastructure.fsh", ShaderType.FragmentShader);
            programOwner = NativeResourceSetup.CreateProgram();
            int program = programOwner.ProgramId;
            GL.AttachShader(program, vertex.ShaderId); GL.AttachShader(program, fragment.ShaderId);
            long start = Stopwatch.GetTimestamp();
        // Action/check: Link the packaged SPIR-V stages, draw the fullscreen quad repeatedly, and verify pixel and SPIR-V flags.
            GL.LinkProgram(program);
            output.WriteLine($"Native program link: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3} ms");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var target = CreateRenderTarget(8, 8, TextureFormat.Rgba32f);
            FramebufferTestSetup.BindForEngineRendering(target);
            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 8; i++) RenderNativeReferenceFullscreenQuad(program);
            output.WriteLine($"Eight draw submissions: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3} ms");
            var pixel = ReadPixel(target, 4, 4);
            Assert.InRange(pixel.R, 0.55f, 0.58f);
            Assert.InRange(pixel.G, 0.55f, 0.58f);
            foreach (int shader in new[] { vertex.ShaderId, fragment.ShaderId })
            {
                GL.GetShader(shader, (ShaderParameter)All.SpirVBinary, out int isSpirv);
                Assert.Equal(1, isSpirv);
            }
        }
        finally
        {
            GL.UseProgram(0);
            programOwner?.Dispose();
            vertex?.Dispose();
            fragment?.Dispose();
        }
    }

    #endregion
    #endregion

    #region Private
    private readonly ITestOutputHelper output;

    /// <summary>Permits only compiled payloads and their required association metadata, never GLSL source.</summary>
    private static void AssertBinaryAsset(string path) =>
        Assert.True(path.EndsWith(".spv", StringComparison.Ordinal) || path == "shaders/" + ShaderBinaryDigest.FileName, path);
    /// <summary>Selects packaged declarations while retaining production binary loading and replacement behavior.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Public API
        /// <summary>Exposes prepared diagnostic locations including inactive contract entries.</summary>
        public IReadOnlyDictionary<string, int> DiagnosticUniformLocations => ResourceBindings.BinaryInterface!.Uniforms;

        /// <summary>Selects the reusable fullscreen fixture without requiring the game's shader factory.</summary>
        public FixtureProgram(string name = "tests/render_infrastructure")
        {
            PassName = name;
        }
        #endregion

        #region Protected
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override VanillaGraphicsExpanded.Rendering.RHI.RHIResourceBindings CaptureBindingsCore() => new(Shader);
        /// <summary>Uses the shared declarations for production resource lookup.</summary>
        protected override VanillaGraphicsExpanded.Rendering.GpuProgramLayout CreateLayout()
        {
            var layout = new VanillaGraphicsExpanded.Rendering.GpuProgramLayout();
            layout.RegisterContract(VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create(ShaderName));
            return layout;
        }
        #endregion
    }


    #endregion
}
