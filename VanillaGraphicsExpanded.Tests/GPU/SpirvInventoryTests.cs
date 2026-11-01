using VanillaGraphicsExpanded.Tests.Helpers;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises registry-projected binaries and declared program combinations without filename inference.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SpirvInventoryTests : IDisposable
{
    private readonly HeadlessGLFixture fixture;
    private readonly ITestOutputHelper output;
    private static string Root => Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
    private static readonly ShaderVariantResolver Registry = ShaderBuildTool.Spirv.TestShaderPrograms.Create();

    /// <summary>Retains the shared context and preparation receipt output.</summary>
    public SpirvInventoryTests(HeadlessGLFixture fixture, ITestOutputHelper output) { this.fixture = fixture; this.output = output; }

    #region Registry coverage
    /// <summary>Loads an explicit compute selection from a borrowed slice without modifying backing storage.</summary>
    [Fact]
    public void SlicedAssetsSpecializeAndLinkWithoutMutation()
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var assets = new VanillaGraphicsExpanded.Tests.Helpers.SlicedShaderAssets(path => TestAssetSnapshot.Built.Copy("shaders/" + path));
        var selected = new ShaderLoadPlan(new ShaderSettings(Registry.FindProgram("tests/GpuProgramLayoutBindingTests_1"))).Stages.Single();
        GpuShaderModule? shader = null; int program = 0;
        try {
            shader = PrecompiledStageFixture.Load(selected, assets.Read);
            program = GL.CreateProgram(); GL.AttachShader(program, shader.ShaderId); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { if (program != 0) GL.DeleteProgram(program); shader?.Dispose(); assets.AssertUnchanged(); }
    }

    /// <summary>Enumerates explicit compute program assignments for ownership regression.</summary>
    public static IEnumerable<object[]> ComputeVariants() => Programs(ShaderStageKind.Compute);

    /// <summary>Enumerates graphics assignments including declared alternate stage pairings.</summary>
    public static IEnumerable<object[]> GraphicsPrograms() => Programs(ShaderStageKind.Fragment);

    /// <summary>Creates serializable test rows from complete supported program assignments.</summary>
    private static IEnumerable<object[]> Programs(ShaderStageKind requiredKind) => Registry.Programs.Values
        .Where(p => p.Stages.Any(s => s.Kind == requiredKind))
        .SelectMany(p => p.Assignments.Select(a => new object[] { p.Identity, ShaderAssignments.Key(a) }));

    /// <summary>Links every compute assignment and retains repeated-disposal coverage on the resource-free compute representative.</summary>
    [Theory]
    [MemberData(nameof(ComputeVariants))]
    public void ComputeAssignmentsLinkAndRepresentativeDisposalPreservesReplacement(string identity, string key)
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var selected = new ShaderLoadPlan(Settings(identity, key)).Stages.Single();
        ComputeShaderAsset? previous = null;
        try {
            for (int generation = 0; generation < (identity == "tests/GpuUniformRingBufferIntegrationTests_1" ? 3 : 1); generation++) {
                var layout = new GpuProgramLayout(); layout.RegisterContract(selected.Stage.Bindings);
                Assert.True(ComputeShaderAsset.TryLoadFromSpirv(Path.Combine(Root, selected.BinaryPath), Settings(identity, key), out var current, out string log, layout: layout, fileSystem: ShaderAssetFileSystem.Built), log);
                using (current) {
                    Assert.NotNull(current); previous?.Dispose(); Assert.True(GL.IsProgram(current.ProgramId));
                    current.Pipeline.RequirePipeline().RequireShader();
                    // The next activation uses OpenGlStateCache; release through that same owner.
                    OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
                    int id = current.ProgramId; current.Dispose(); Assert.False(GL.IsProgram(id));
                    Assert.Equal(ErrorCode.NoError, GL.GetError()); previous = current;
                }
            }
        }
        finally { previous?.Dispose(); }
    }

    /// <summary>Enumerates every deduplicated stage binary directly from the resolver.</summary>
    public static IEnumerable<object[]> Variants() => Registry.Binaries.Select(b => new object[] { b.Stage.Identity, b.Key });

    /// <summary>Specializes every compiled stage; explicit program rows separately verify all stage combinations.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void BuiltVariantSpecializes(string identity, string key)
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var selected = Assert.Single(Registry.Binaries, b => b.Stage.Identity == identity && b.Key == key);
        using var shader = PrecompiledStageFixture.Load(selected, Read);
        Assert.True(GL.IsShader(shader.ShaderId));
    }

    /// <summary>Links every explicit graphics pairing from one coherent settings snapshot.</summary>
    [Theory]
    [MemberData(nameof(GraphicsPrograms))]
    public void DeclaredGraphicsCombinationLinks(string identity, string key)
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var handles = new List<GpuShaderModule>(); int program = 0;
        try {
            foreach (var stage in new ShaderLoadPlan(Settings(identity, key)).Stages) handles.Add(PrecompiledStageFixture.Load(stage, Read));
            program = GL.CreateProgram(); foreach (var shader in handles) GL.AttachShader(program, shader.ShaderId);
            long started = Stopwatch.GetTimestamp(); GL.LinkProgram(program);
            output.WriteLine($"Link only: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, identity + " [" + key + "]: " + GL.GetProgramInfoLog(program));
        }
        finally { if (program != 0) GL.DeleteProgram(program); foreach (var shader in handles) shader.Dispose(); }
    }

    /// <summary>Restores a complete canonical test assignment through the production settings validator.</summary>
    private static ShaderSettings Settings(string identity, string key) => new(Registry.FindProgram(identity), key.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => (string?)p[1], StringComparer.Ordinal));

    /// <summary>Reads the copied build assets used by production-style tests.</summary>
    private static ReadOnlySpan<byte> Read(string path) => TestAssetSnapshot.Built.Read("shaders/" + path);

    /// <summary>The shared collection owns the GL context.</summary>
    public void Dispose() { }
    #endregion
}
