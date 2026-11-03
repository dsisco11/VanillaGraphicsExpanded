using System;
using System.Collections.Generic;
using System.IO;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Verifies that LumOn shader programs expose the expected UBO blocks.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnUboBindingTests
{
    private const int FrameBinding = 17;
    private const int WorldProbeBinding = 13;

    private readonly HeadlessGLFixture fixture;
    private readonly ITestOutputHelper output;
    private readonly bool assetsAvailable;

    #region Public API

    /// <summary>Creates binding tests and records whether their built shader assets are available.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    /// <param name="output">The test output writer.</param>
    public LumOnUboBindingTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        string includePath = Path.Combine(shaderPath, "includes");
        assetsAvailable = TestAssetSnapshot.HasDirectory(shaderPath) && TestAssetSnapshot.HasDirectory(includePath);

    }

    /// <summary>Gets shader pairs whose frame block declarations are checked by the test cases.</summary>
    public static TheoryData<string, string, string, bool> FrameUboShaderPairs => new()
    {
        { "lumon_velocity", "lumon_velocity.vsh", "lumon_velocity.fsh", true },
        { "lumon_probe_anchor", "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh", true },
        { "lumon_probe_atlas_trace", "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh", true },
        { "lumon_probe_atlas_temporal", "lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh", true },
        { "lumon_probe_atlas_filter", "lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh", true },
        { "lumon_probe_atlas_project_sh", "lumon_probe_atlas_project_sh.vsh", "lumon_probe_atlas_project_sh.fsh", true },
        { "lumon_probe_atlas_project_sh9", "lumon_probe_atlas_project_sh9.vsh", "lumon_probe_atlas_project_sh9.fsh", true },
        { "lumon_probe_atlas_gather", "lumon_probe_atlas_gather.vsh", "lumon_probe_atlas_gather.fsh", true },
        { "lumon_probe_sh9_gather", "lumon_probe_sh9_gather.vsh", "lumon_probe_sh9_gather.fsh", true },
        { "lumon_upsample", "lumon_upsample.vsh", "lumon_upsample.fsh", true },
        { "lumon_combine", "lumon_combine.vsh", "lumon_combine.fsh", false },
        { "lumon_debug_view_scene_depth", "lumon_debug.vsh", "lumon_debug_view_scene_depth.fsh", false },
        { "lumon_debug_view_world_probe_irradiance_combined", "lumon_debug.vsh", "lumon_debug_view_world_probe_irradiance_combined.fsh", false },
    };

    /// <summary>Verifies a shader exposes the expected LumOn frame uniform block.</summary>
    /// <param name="programIdentity">The shader contract identity.</param>
    /// <param name="vertexShader">The vertex shader asset.</param>
    /// <param name="fragmentShader">The fragment shader asset.</param>
    /// <param name="consumesEffectFrame">Whether the shader consumes the effect-frame values.</param>
    [Theory]
    [MemberData(nameof(FrameUboShaderPairs))]
    public void Shader_Exposes_LumOnFrameUBO(string programIdentity, string vertexShader, string fragmentShader, bool consumesEffectFrame)
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(!assetsAvailable, "Shader build assets are not available");
        using var shader = PrepareProgram(programIdentity, vertexShader, fragmentShader);

        if (consumesEffectFrame)
        {
            AssertUniformBlockPresent(shader, "LumOnFrameUBO");
            AssertUniformBlockBindingMatchesLayoutWhenAvailable(shader, "LumOnFrameUBO", FrameBinding);
        }
        else
        {
            // Camera-only consumers deliberately have no active effect frame storage.
            Assert.Equal(-1, shader.Interface.GetUniformBlockIndex("LumOnFrameUBO"));
            AssertUniformBlockPresent(shader, "VgeFrameUBO");
            AssertUniformBlockBindingMatchesLayoutWhenAvailable(shader, "VgeFrameUBO", 12);
        }
    }

    /// <summary>Verifies world-probe enabled shaders expose their world-probe uniform block.</summary>
    [Fact]
    public void Shader_Exposes_LumOnWorldProbeUBO_WhenWorldProbeEnabled()
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(!assetsAvailable, "Shader build assets are not available");
        var defines = new Dictionary<string, string?>
        {
            // Ensure the world-probe code path is compiled in and UBO-backed accessors are referenced.
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1",
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "8",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "16.0",
        };

        using var shader = PrepareProgram("lumon_probe_atlas_trace", "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh", defines);
        AssertUniformBlockPresent(shader, "LumOnWorldProbeUBO");
        AssertUniformBlockBindingMatchesLayoutWhenAvailable(shader, "LumOnWorldProbeUBO", WorldProbeBinding);
    }

    #endregion

    #region Private

    /// <summary>Prepares a declared graphics executable from its immutable build outputs and retains its ready owner.</summary>
    private static RHIShader PrepareProgram(string programIdentity, string vertex, string fragment,
        IReadOnlyDictionary<string, string?>? defines = null)
    {
        var program = GpuShaderContracts.Registry.FindProgram(programIdentity);
        if (!program.Stages.Any(stage => stage.Kind == ShaderStageKind.Vertex && stage.Identity == vertex) ||
            !program.Stages.Any(stage => stage.Kind == ShaderStageKind.Fragment && stage.Identity == fragment))
            throw new InvalidOperationException($"Program '{programIdentity}' does not declare shader pair '{vertex}' and '{fragment}'.");
        using var candidate = new ShaderAssetCandidate(new(new ShaderSettings(program, defines)),
            path => TestAssetSnapshot.Built.Read("shaders/" + path),
            () => ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName)));
        candidate.Complete();
        return candidate.TakeShader();
    }

    /// <summary>Asserts that the prepared shader reports the named uniform block.</summary>
    private void AssertUniformBlockPresent(RHIShader shader, string blockName)
    {
        int programId = shader.Program.ProgramId;
        int blockIndex = shader.Interface.GetUniformBlockIndex(blockName);
        Assert.True(blockIndex >= 0, $"Program {programId} did not expose uniform block '{blockName}'.");

        output.WriteLine($"Program {programId}: {blockName} index={blockIndex}");
    }

    /// <summary>Checks a present uniform block against its declared layout binding when metadata is available.</summary>
    private static void AssertUniformBlockBindingMatchesLayoutWhenAvailable(RHIShader shader, string blockName, int expectedBinding)
    {
        int programId = shader.Program.ProgramId;
        // SPIR-V may omit debug names, so resolve the numeric block index from the retained executable metadata.
        int blockIndex = shader.Interface.GetUniformBlockIndex(blockName);
        Assert.True(blockIndex >= 0, $"Program {programId} did not expose uniform block '{blockName}'.");

        // Inspect the binary's original binding before any runtime assignment can hide a mismatch.
        GL.GetActiveUniformBlock(programId, blockIndex, ActiveUniformBlockParameter.UniformBlockBinding, out int binding);
        Assert.Equal(expectedBinding, binding);

        // Prefer testing the cached layout as well when supported by the GL context.
        var layout = new GpuProgramLayout { BinaryInterface = shader.Interface };
        layout.RebuildCache(programId);
        Assert.SkipWhen(layout.UniformBlockBindings.Count == 0, "Program interface queries unavailable; skipping layout-cache assertions.");

        Assert.True(
            layout.UniformBlockBindings.TryGetValue(blockName, out int cachedBinding),
            $"GpuProgramLayout cache did not include uniform block '{blockName}'.");
        Assert.Equal(expectedBinding, cachedBinding);
    }
    #endregion
}
