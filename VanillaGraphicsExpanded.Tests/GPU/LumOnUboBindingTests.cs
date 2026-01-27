using System;
using System.Collections.Generic;
using System.IO;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Phase 23: Validates that migrated LumOn shader programs expose the expected UBO blocks.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnUboBindingTests : IDisposable
{
    private const int FrameBinding = 12;
    private const int WorldProbeBinding = 13;

    private readonly HeadlessGLFixture fixture;
    private readonly ShaderTestHelper? helper;
    private readonly ITestOutputHelper output;

    public LumOnUboBindingTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;

        if (fixture.IsContextValid)
        {
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
            var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

            if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
            {
                helper = new ShaderTestHelper(shaderPath, includePath);
            }
        }
    }

    public void Dispose()
    {
        helper?.Dispose();
    }

    public static TheoryData<string, string> FrameUboShaderPairs => new()
    {
        { "lumon_velocity.vsh", "lumon_velocity.fsh" },
        { "lumon_probe_anchor.vsh", "lumon_probe_anchor.fsh" },
        { "lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh" },
        { "lumon_probe_atlas_temporal.vsh", "lumon_probe_atlas_temporal.fsh" },
        { "lumon_probe_atlas_filter.vsh", "lumon_probe_atlas_filter.fsh" },
        { "lumon_probe_atlas_project_sh.vsh", "lumon_probe_atlas_project_sh.fsh" },
        { "lumon_probe_atlas_project_sh9.vsh", "lumon_probe_atlas_project_sh9.fsh" },
        { "lumon_probe_atlas_gather.vsh", "lumon_probe_atlas_gather.fsh" },
        { "lumon_probe_sh9_gather.vsh", "lumon_probe_sh9_gather.fsh" },
        { "lumon_upsample.vsh", "lumon_upsample.fsh" },
        { "lumon_combine.vsh", "lumon_combine.fsh" },
        { "lumon_debug.vsh", "lumon_debug.fsh" },
        { "lumon_debug_worldprobe.vsh", "lumon_debug_worldprobe.fsh" },
    };

    [Theory]
    [MemberData(nameof(FrameUboShaderPairs))]
    public void Shader_Exposes_LumOnFrameUBO(string vertexShader, string fragmentShader)
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(helper == null, "ShaderTestHelper not available - assets may be missing");

        var linkResult = helper!.CompileAndLink(vertexShader, fragmentShader);
        Assert.True(linkResult.IsSuccess, linkResult.ErrorMessage);

        AssertUniformBlockPresent(linkResult.ProgramId, "LumOnFrameUBO");
        AssertUniformBlockBindingMatchesLayoutWhenAvailable(linkResult.ProgramId, "LumOnFrameUBO", FrameBinding);
    }

    [Fact]
    public void Shader_Exposes_LumOnWorldProbeUBO_WhenWorldProbeEnabled()
    {
        fixture.EnsureContextValid();
        Assert.SkipWhen(helper == null, "ShaderTestHelper not available - assets may be missing");

        var defines = new Dictionary<string, string?>
        {
            // Ensure the world-probe code path is compiled in and UBO-backed accessors are referenced.
            ["VGE_LUMON_WORLDPROBE_ENABLED"] = "1",
            ["VGE_LUMON_WORLDPROBE_LEVELS"] = "1",
            ["VGE_LUMON_WORLDPROBE_RESOLUTION"] = "8",
            ["VGE_LUMON_WORLDPROBE_BASE_SPACING"] = "16.0",
        };

        var linkResult = helper!.CompileAndLink("lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh", defines);
        Assert.True(linkResult.IsSuccess, linkResult.ErrorMessage);

        AssertUniformBlockPresent(linkResult.ProgramId, "LumOnWorldProbeUBO");
        AssertUniformBlockBindingMatchesLayoutWhenAvailable(linkResult.ProgramId, "LumOnWorldProbeUBO", WorldProbeBinding);
    }

    private void AssertUniformBlockPresent(int programId, string blockName)
    {
        int blockIndex = GL.GetUniformBlockIndex(programId, blockName);
        Assert.True(blockIndex >= 0, $"Program {programId} did not expose uniform block '{blockName}'.");

        output.WriteLine($"Program {programId}: {blockName} index={blockIndex}");
    }

    private static void AssertUniformBlockBindingMatchesLayoutWhenAvailable(int programId, string blockName, int expectedBinding)
    {
        // GL 3.3: direct query.
        int blockIndex = GL.GetUniformBlockIndex(programId, blockName);
        Assert.True(blockIndex >= 0, $"Program {programId} did not expose uniform block '{blockName}'.");

        // GLSL 330 can't fix bindings in-source; production code assigns them via glUniformBlockBinding.
        // Mirror that here so we can validate the contract binding indices (12/13).
        GL.UniformBlockBinding(programId, blockIndex, expectedBinding);

        GL.GetActiveUniformBlock(programId, blockIndex, ActiveUniformBlockParameter.UniformBlockBinding, out int binding);
        Assert.Equal(expectedBinding, binding);

        // Prefer testing the cached layout as well when supported by the GL context.
        var layout = GpuProgramLayout.TryBuild(programId);
        Assert.SkipWhen(layout.UniformBlockBindings.Count == 0, "Program interface queries unavailable; skipping layout-cache assertions.");

        Assert.True(
            layout.UniformBlockBindings.TryGetValue(blockName, out int cachedBinding),
            $"GpuProgramLayout cache did not include uniform block '{blockName}'.");
        Assert.Equal(expectedBinding, cachedBinding);
    }
}
