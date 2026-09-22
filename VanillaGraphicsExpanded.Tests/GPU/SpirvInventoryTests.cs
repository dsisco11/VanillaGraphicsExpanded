using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the entire built inventory independently of which variants behavioral fixtures select.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SpirvInventoryTests : IDisposable
{
    private readonly HeadlessGLFixture fixture;
    private readonly ITestOutputHelper output;
    private static string Root => Path.Combine(AppContext.BaseDirectory, "assets", "shaders");

    /// <summary>Uses the graphics context and records preparation timings in the test receipt.</summary>
    public SpirvInventoryTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    { this.fixture = fixture; this.output = output; }

    #region Inventory coverage
    /// <summary>Loads and links a built shader from sliced assets without modifying their backing storage.</summary>
    [Fact]
    public void SlicedAssetsSpecializeAndLinkWithoutMutation()
    {
        fixture.EnsureContextValid();
        var assets = new VanillaGraphicsExpanded.Tests.Helpers.SlicedShaderAssets(
            path => File.ReadAllBytes(Path.Combine(Root, path)));
        int shader = 0, program = 0;
        try
        {
            shader = SpirvStageLoader.Load("tests/GpuProgramLayoutBindingTests_1.csh",
                ShaderType.ComputeShader, null, assets.Read).Shader;
            program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (shader != 0) GL.DeleteShader(shader);
            assets.AssertUnchanged();
        }
    }

    /// <summary>Discovers compute variants for their separate pipeline ownership regression.</summary>
    public static IEnumerable<object[]> ComputeVariants() => Variants()
        .Where(row => (string)row[1] == "csh")
        .Select(row => new object[] { row[0], row[2] });

    /// <summary>Recreates every compute variant and ensures repeated disposal cannot delete its replacement.</summary>
    [Theory]
    [MemberData(nameof(ComputeVariants))]
    public void ComputeDisposalDoesNotInvalidateReplacement(string source, string key)
    {
        fixture.EnsureContextValid();
        var configuration = key.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => (string?)pair[1], StringComparer.Ordinal);
        VanillaGraphicsExpanded.Rendering.GpuComputePipeline? previous = null;
        try
        {
            for (int generation = 0; generation < 3; generation++)
            {
                var loaded = SpirvStageLoader.Load(source, ShaderType.ComputeShader, configuration, Read);
                using var module = new VanillaGraphicsExpanded.Rendering.GpuShaderModule(loaded.Shader, ShaderType.ComputeShader)
                { BindingContract = loaded.Contract };
                var layout = new VanillaGraphicsExpanded.Rendering.GpuProgramLayout();
                layout.RegisterContract(VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create(Path.ChangeExtension(source, null)));
                Assert.True(VanillaGraphicsExpanded.Rendering.GpuComputePipeline.TryCreate(module, out var current, out string log, layout: layout), log);
                using (current)
                {
                    Assert.NotNull(current);
                    // The old owner must be idempotent even if GL recycled its former numeric ID.
                    previous?.Dispose();
                    Assert.True(GL.IsProgram(current.ProgramId));
                    current.Use();
                    Assert.Equal(current.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
                    GL.UseProgram(0);
                    int id = current.ProgramId;
                    current.Dispose();
                    Assert.False(GL.IsProgram(id));
                    Assert.Equal(ErrorCode.NoError, GL.GetError());
                    previous = current;
                }
            }
        }
        finally { previous?.Dispose(); }
    }

    /// <summary>Discovers every built stage variant; additions automatically become required GPU cases.</summary>
    public static IEnumerable<object[]> Variants()
    {
        foreach (string file in Directory.GetFiles(Root, "*.spv", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (relative.StartsWith("variants/", StringComparison.Ordinal)) continue;
            string source = relative[..^4];
            var contract = VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.CreateStage(source);
            foreach (var variant in contract.Variants()) yield return [source, Path.GetExtension(source)[1..], contract.VariantKey(variant)];
        }
    }
    /// <summary>Loads every variant and links fragment/compute entries with their actual stage interfaces.</summary>
    [Theory]
    [MemberData(nameof(Variants))]
    public void BuiltVariantSpecializesAndLinks(string source, string stage, string key)
    {
        fixture.EnsureContextValid();
        var configuration = key.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => (string?)pair[1], StringComparer.Ordinal);
        ShaderType type = stage switch
        {
            "vsh" => ShaderType.VertexShader, "fsh" => ShaderType.FragmentShader, "csh" => ShaderType.ComputeShader,
            _ => throw new InvalidOperationException("Add an explicit stage-pair fixture for the new inventory stage: " + stage)
        };
        var handles = new List<int>();
        int program = 0;
        try
        {
            var loaded = SpirvStageLoader.Load(source, type, configuration, Read);
            handles.Add(loaded.Shader);
            output.WriteLine("Selected stage: " + SpirvStageLoader.LastTiming);
            if (stage == "vsh") return; // Every vertex binary is specialized; its program pairs are linked by fragment cases.
            if (stage == "fsh")
            {
                string vertex = VertexFor(source);
                handles.Add(SpirvStageLoader.Load(vertex, ShaderType.VertexShader, configuration, Read).Shader);
                output.WriteLine("Vertex stage: " + SpirvStageLoader.LastTiming);
            }
            program = GL.CreateProgram();
            foreach (int handle in handles) GL.AttachShader(program, handle);
            long started = Stopwatch.GetTimestamp(); GL.LinkProgram(program);
            output.WriteLine($"Link only: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms");
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, source + " [" + key + "]: " + GL.GetProgramInfoLog(program));
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            foreach (int handle in handles) GL.DeleteShader(handle);
        }
    }

    /// <summary>Uses matching stage names except for the explicitly shared production and test vertex programs.</summary>
    internal static string VertexFor(string fragment) => fragment switch
    {
        "lumon_probe_atlas_pis_mask.fsh" => "lumon_probe_atlas_trace.vsh",
        "tests/GlStateCacheUnbindIntegrationTests_2.fsh" => "tests/GlStateCacheUnbindIntegrationTests_1.vsh",
        "tests/PbrMaterialParamsTextureSmokeTests_2.fsh" => "tests/PbrMaterialParamsTextureSmokeTests_1.vsh",
        "tests/framebuffer_blend.fsh" => "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh",
        _ when fragment.StartsWith("pbr_heightbake_", StringComparison.Ordinal) => "pbr_heightbake_fullscreen.vsh",
        _ => Path.ChangeExtension(fragment, "vsh")
    };

    /// <summary>Reads the same copied build assets that normal production-style tests use.</summary>
    private static ReadOnlySpan<byte> Read(string path) => File.ReadAllBytes(Path.Combine(Root, path));

    /// <summary>The collection fixture owns the shared context.</summary>
    public void Dispose() { }
    #endregion
}
