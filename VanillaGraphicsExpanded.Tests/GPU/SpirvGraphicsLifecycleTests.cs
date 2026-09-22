using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;
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
    /// <summary>Automatically includes every graphics program variant in the built shader inventory.</summary>
    public static IEnumerable<object[]> GraphicsVariants() => SpirvInventoryTests.Variants()
        .Where(row => (string)row[1] == "fsh")
        .Select(row => new object[] { Path.ChangeExtension((string)row[0], null), row[2] });

    /// <summary>Reuses the registered program after the engine has disposed its previous GL generation.</summary>
    [Theory]
    [MemberData(nameof(GraphicsVariants))]
    public void RuntimeReloadAfterEngineDisposalDoesNotDeleteStaleHandles(string shaderName, string variant)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var program = new FixtureProgram(shaderName);
        foreach (string setting in variant.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = setting.Split('=', 2);
            program.SetDefine(pair[0], pair[1]);
        }
        program.Initialize(assets.Api);
        try
        {
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            GL.UseProgram(0);
            int previous = program.ProgramId;
            int vertex = program.VertexShader.ShaderId, fragment = program.FragmentShader.ShaderId;
            program.Dispose();
            Assert.False(GL.IsProgram(previous));
            Assert.False(GL.IsShader(vertex));
            Assert.False(GL.IsShader(fragment));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            Assert.True(GL.IsProgram(program.ProgramId));
            Assert.False(program.Disposed);
            // Engine Use also touches the live renderer singleton, unavailable in a headless fixture.
            // Verify the GL binding directly; Disposed above checks its engine-side lifetime guard.
            GL.UseProgram(program.ProgramId);
            Assert.Equal(program.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            GL.UseProgram(0);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            int reloaded = program.ProgramId;
            program.Dispose();
            Assert.False(GL.IsProgram(reloaded));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            // A second cycle verifies that resetting the engine's disposed flag restores future disposal too.
            Assert.True(program.CompileAndLink(), string.Join("\n", assets.Logs));
            Assert.False(program.Disposed);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { program.Dispose(); }
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
            Assert.True(program.Compile());
            Assert.NotEqual(first, program.ProgramId);
            Assert.False(GL.IsProgram(first));
            int current = program.ProgramId;
            const string source = "shaders/tests/render_infrastructure.fsh.spv";
            assets.Overrides[source] = new byte[20];
            Assert.False(program.Compile());
            Assert.Equal(current, program.ProgramId);
            Assert.True(GL.IsProgram(current));
            assets.Overrides.Clear();
            Assert.True(program.Compile());
            output.WriteLine($"Runtime reload driver link: {program.LastSpirvLinkMilliseconds:F3} ms");
        }
        finally
        {

            program.Dispose();
        }
    }

    /// <summary>Installs engine stage objects while retaining production binary loading and reload behavior.</summary>
    private sealed class FixtureProgram : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        /// <summary>Selects the reusable fullscreen fixture without requiring the game's shader factory.</summary>
        public FixtureProgram(string name = "tests/render_infrastructure")
        {
            PassName = name;
            VertexShader = new Vintagestory.Client.NoObf.Shader();
            FragmentShader = new Vintagestory.Client.NoObf.Shader();
        }

        /// <summary>Pairs shared production/test vertex stages with their actual fragment entrypoints.</summary>
        protected override string VertexStageShaderName => Path.ChangeExtension(SpirvInventoryTests.VertexFor(PassName + ".fsh"), null);

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
