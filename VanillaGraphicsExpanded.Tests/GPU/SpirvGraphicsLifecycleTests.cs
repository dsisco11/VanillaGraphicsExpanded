using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks binary-only graphics execution and records driver preparation separately from drawing.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SpirvGraphicsLifecycleTests : RenderTestBase
{
    private readonly ITestOutputHelper output;

    /// <summary>Uses the shared GL context and test receipt output.</summary>
    public SpirvGraphicsLifecycleTests(HeadlessGLFixture fixture, ITestOutputHelper output) : base(fixture) => this.output = output;

    #region Binary execution
    /// <summary>Includes the built graphics variants owned by runtime program declarations.</summary>
    public static IEnumerable<object[]> GraphicsVariants() => SpirvInventoryTests.GraphicsPrograms()
        .Where(row => VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.Programs.ContainsKey((string)row[0]));

    /// <summary>Replaces every packaged variant through independent asset invalidation and targeted retirement.</summary>
    [Theory]
    [MemberData(nameof(GraphicsVariants))]
    public void RuntimeReplacementRetiresOnlyPreviousExecutable(string shaderName, string variant)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        // Replacement uses only packaged binary assets.
        assets.BeforeRead = AssertBinaryAsset;
        var program = new FixtureProgram(shaderName);
        program.SetDefines(variant.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(setting => setting.Split('=', 2)).ToDictionary(pair => pair[0], pair => (string?)pair[1]));
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            GL.UseProgram(0);
            int previous = program.ProgramId;
            program.InvalidateAssets();
            Assert.True(GL.IsProgram(previous));
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            Assert.True(GL.IsProgram(program.ProgramId));
            Assert.False(program.IsRetired);
            // Verify the linked executable directly without publishing this fixture's intentionally absent inputs.
            GL.UseProgram(program.ProgramId);
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            GL.UseProgram(0);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            int reloaded = program.ProgramId;
            program.InvalidateAssets();
            Assert.True(GL.IsProgram(reloaded));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            // A second replacement checks monotonic ownership beyond the first install.
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            Assert.False(program.IsRetired);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { program.Dispose(); }
    }

    /// <summary>Retains active and optimized-out diagnostic locations from compiled contracts without engine storage.</summary>
    [Fact]
    public void BinaryOnlyReloadPreservesContractUniformDictionary()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        assets.BeforeRead = AssertBinaryAsset;
        using var program = new FixtureProgram("lumon_debug_view_direct_total");
        program.Initialize(assets.Api);
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
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var program = new FixtureProgram();
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            int first = program.ProgramId;
            var firstTable = program.ResourceBindings.BinaryInterface!.PreparedBindings;
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
            output.WriteLine($"Runtime reload driver link: {program.LastSpirvLinkMilliseconds:F3} ms");
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
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var program = new FixtureProgram();
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join('\n', assets.Logs));
        int installed = program.ProgramId;
        var settings = program.InstalledSettings;
        var layout = program.ResourceBindings;
        var table = layout.BinaryInterface!.PreparedBindings;
        if (substitute.Length == 0)
            assets.BeforeRead = path => { if (path == "shaders/" + replaced) throw new IOException("controlled asset read failure"); };
        else
        {
            assets.Overrides["shaders/" + replaced] = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", substitute));
            // Keep the substituted bytes and declarations coherent so this deliberately invalid
            // stage pairing still exercises specialization, linking or binding-contract rejection.
            var manifest = ShaderBinaryDigest.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
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

    /// <summary>Permits only compiled payloads and their required association metadata, never GLSL source.</summary>
    private static void AssertBinaryAsset(string path) =>
        Assert.True(path.EndsWith(".spv", StringComparison.Ordinal) || path == "shaders/" + ShaderBinaryDigest.FileName, path);
    /// <summary>Selects packaged declarations while retaining production binary loading and replacement behavior.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Submission
        /// <summary>Retains this owner's explicit external input publication contract.</summary>
        protected override void Submit() { }
        #endregion

        /// <summary>Exposes prepared diagnostic locations including inactive contract entries.</summary>
        public IReadOnlyDictionary<string, int> DiagnosticUniformLocations => ResourceBindings.BinaryInterface!.Uniforms;

        /// <summary>Selects the reusable fullscreen fixture without requiring the game's shader factory.</summary>
        public FixtureProgram(string name = "tests/render_infrastructure")
        {
            PassName = name;
        }

        /// <summary>Uses the shared declarations for production resource lookup.</summary>
        protected override VanillaGraphicsExpanded.Rendering.GpuProgramLayout CreateLayout()
        {
            var layout = new VanillaGraphicsExpanded.Rendering.GpuProgramLayout();
            layout.RegisterContract(VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create(ShaderName));
            return layout;
        }
    }

    /// <summary>Repeated draws retain SPIR-V shader objects and produce the expected interpolated pixel.</summary>
    [Fact]
    public void RepeatedDrawsUseBuiltBinaries()
    {
        EnsureContextValid();
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = BuiltShaderFixture.Load("tests/render_infrastructure.vsh", ShaderType.VertexShader);
            output.WriteLine("Vertex: " + SpirvStageLoader.LastTiming);
            fragment = BuiltShaderFixture.Load("tests/render_infrastructure.fsh", ShaderType.FragmentShader);
            output.WriteLine("Fragment: " + SpirvStageLoader.LastTiming);
            program = GL.CreateProgram();
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
            long start = Stopwatch.GetTimestamp();
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.LinkProgram(program);
            output.WriteLine($"Link and numeric reflection: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3} ms");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            using var target = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba32f);
            target.BindWithViewport();
            start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 8; i++) RenderFullscreenQuad(program);
            GL.Finish();
            output.WriteLine($"Eight draws plus completion: {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F3} ms");
            var pixel = ReadPixel(target, 4, 4);
            Assert.InRange(pixel.R, 0.55f, 0.58f);
            Assert.InRange(pixel.G, 0.55f, 0.58f);
            foreach (int shader in new[] { vertex, fragment })
            {
                GL.GetShader(shader, (ShaderParameter)All.SpirVBinary, out int isSpirv);
                Assert.Equal(1, isSpirv);
            }
        }
        finally
        {
            GL.UseProgram(0);
            if (program != 0) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program);
            if (vertex != 0) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(vertex);
            if (fragment != 0) global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(fragment);
        }
    }
    #endregion
}
