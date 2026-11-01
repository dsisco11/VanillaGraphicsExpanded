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

    #region Public API
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

    /// <summary>Retains native pairings not identically exercised by the production runtime matrix.</summary>
    public static IEnumerable<object[]> NativeGraphicsPrograms() => GraphicsPrograms()
        .Where(row => !RuntimeCovers((string)row[0], (string)row[1]));

    /// <summary>Links every compute assignment and retains repeated-disposal coverage on the resource-free compute representative.</summary>
    [Theory]
    [MemberData(nameof(ComputeVariants))]
    public void ComputeAssignmentsLinkAndRepresentativeDisposalPreservesReplacement(string identity, string key)
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var selected = new ShaderLoadPlan(Settings(Registry.FindProgram(identity), key)).Stages.Single();
        ComputeShaderAsset? previous = null;
        try {
            for (int generation = 0; generation < (identity == "tests/GpuUniformRingBufferIntegrationTests_1" ? 3 : 1); generation++) {
                var layout = new GpuProgramLayout(); layout.RegisterContract(selected.Stage.Bindings);
                Assert.True(ComputeShaderAsset.TryLoadFromSpirv(Path.Combine(Root, selected.BinaryPath), Settings(Registry.FindProgram(identity), key), out var current, out string log, layout: layout, fileSystem: ShaderAssetFileSystem.Built), log);
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

    /// <summary>Links fixture-only or differing graphics pairings not covered by runtime admission.</summary>
    [Theory]
    [MemberData(nameof(NativeGraphicsPrograms))]
    public void DeclaredGraphicsCombinationLinks(string identity, string key)
    {
        fixture.EnsureContextValid();
        using var freshStages = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(null);
        var handles = new List<GpuShaderModule>(); int program = 0;
        try {
            foreach (var stage in new ShaderLoadPlan(Settings(Registry.FindProgram(identity), key)).Stages) handles.Add(PrecompiledStageFixture.Load(stage, Read));
            program = GL.CreateProgram(); foreach (var shader in handles) GL.AttachShader(program, shader.ShaderId);
            long started = Stopwatch.GetTimestamp(); GL.LinkProgram(program);
            output.WriteLine($"Link only: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, identity + " [" + key + "]: " + GL.GetProgramInfoLog(program));
        }
        finally { if (program != 0) GL.DeleteProgram(program); foreach (var shader in handles) shader.Dispose(); }
    }

    #endregion

    /// <summary>The shared collection owns the GL context.</summary>
    public void Dispose() { }
    #endregion

    #region Private
    /// <summary>Creates serializable test rows from complete supported program assignments.</summary>
    private static IEnumerable<object[]> Programs(ShaderStageKind requiredKind) => Registry.Programs.Values
        .Where(p => p.Stages.Any(s => s.Kind == requiredKind))
        .SelectMany(p => p.Assignments.Select(a => new object[] { p.Identity, ShaderAssignments.Key(a) }));

    /// <summary>Restores a complete canonical test assignment through the production settings validator.</summary>
    private static ShaderSettings Settings(GpuShaderContract contract, string key) => new(contract, key.Split(';', StringSplitOptions.RemoveEmptyEntries)
        .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => (string?)p[1], StringComparer.Ordinal));

    /// <summary>Requires the same assignment, stage declarations, binary paths and typed specialization bits.</summary>
    private static bool RuntimeCovers(string identity, string key)
    {
        if (!GpuShaderContracts.Registry.Programs.TryGetValue(identity, out var runtime) ||
            !runtime.Assignments.Any(assignment => ShaderAssignments.Key(assignment) == key)) return false;
        var nativeStages = new ShaderLoadPlan(Settings(Registry.FindProgram(identity), key)).Stages;
        var runtimeStages = new ShaderLoadPlan(Settings(runtime, key)).Stages;
        // SameInputs includes kind, entry point, structural declarations and active specialization IDs/types/bits.
        return nativeStages.Count == runtimeStages.Count && nativeStages.Zip(runtimeStages)
            .All(pair => pair.First.BinaryPath == pair.Second.BinaryPath && pair.First.SameInputs(pair.Second));
    }

    /// <summary>Reads the copied build assets used by production-style tests.</summary>
    private static ReadOnlySpan<byte> Read(string path) => TestAssetSnapshot.Built.Read("shaders/" + path);

    #endregion
}
