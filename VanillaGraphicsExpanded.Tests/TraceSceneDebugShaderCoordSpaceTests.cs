using System;
using System.IO;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class TraceSceneDebugShaderCoordSpaceTests
{
    /// <summary>Reads the exact shader source captured by the test build, without checkout discovery.</summary>
    private static string ReadRepoFileOrSkip(string relativePath)
    {
        const string prefix = "VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/";
        string normalized = relativePath.Replace('\\', '/');
        Assert.StartsWith(prefix, normalized);
        return TestAssetSnapshot.Built.Text(normalized[prefix.Length..]);
    }

    [Fact]
    public void TraceSceneDebug_UsesSharedFrameWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes", "debug", "trace_scene_debug_surface_cell.glsl"));


        Assert.Contains("VgeFrameMatrixSpaceToWorldCell", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenesOverviewDebug_UsesSharedFrameWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "lumon_debug_view_lum_on_scenes_overview.fsh"));

        Assert.Contains("traceSceneDebugSurfaceCell", src, StringComparison.Ordinal);
    }

    [Fact]
    public void SurfaceCache_IrradianceSampling_DoesNotRejectNeedsRelight()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumonscene_surface_cache.glsl"));

        // We only reject NeedsCapture; NeedsRelight must still allow sampling.
        Assert.Contains("VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE", src, StringComparison.Ordinal);
        Assert.DoesNotContain("VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE | VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT", src, StringComparison.Ordinal);
    }

    [Fact]
    public void RelightShader_UsesCpuBatchIndex_ForDeterministicCoverage()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "lumonscene_relight_voxel_dda.csh"));

        Assert.Contains("uint batchIndexIn = w.z", src, StringComparison.Ordinal);
        Assert.Contains("Squirrel3HashU(physicalPageId, virtualPageIndex, linear)", src, StringComparison.Ordinal);
    }
}
