using System;
using System.IO;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class TraceSceneDebugShaderCoordSpaceTests
{
    private static string ReadRepoFileOrSkip(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            string sln = Path.Combine(dir.FullName, "VanillaGraphicsExpanded.sln");
            if (File.Exists(sln))
            {
                string full = Path.Combine(dir.FullName, relativePath);
                if (!File.Exists(full))
                {
                    break;
                }

                return File.ReadAllText(full);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException($"Repo root not found for test file: {relativePath}");
    }

    [Fact]
    public void TraceSceneDebug_UsesSharedFrameWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumon_debug_tracescene.glsl"));

        Assert.Contains("@import \"./lumon_frame_worldspace_bridge.glsl\"", src, StringComparison.Ordinal);
        Assert.Contains("LumonFrameMatrixSpacePosToWorldCell", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenesOverviewDebug_UsesSharedFrameWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumon_debug_scenes_overview.glsl"));

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
