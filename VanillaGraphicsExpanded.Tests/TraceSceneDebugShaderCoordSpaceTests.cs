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
    public void TraceSceneDebug_UsesTerrainBridgeWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumon_debug_tracescene.glsl"));

        Assert.Contains("@import \"./vge_worldspace_bridge.glsl\"", src, StringComparison.Ordinal);
        Assert.Contains("VgeMatrixSpacePosToWorldCell", src, StringComparison.Ordinal);
    }

    [Fact]
    public void ScenesOverviewDebug_UsesTerrainBridgeWorldspaceConversion()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumon_debug_scenes_overview.glsl"));

        Assert.Contains("@import \"./vge_worldspace_bridge.glsl\"", src, StringComparison.Ordinal);
        Assert.Contains("VgeMatrixSpacePosToWorldCell", src, StringComparison.Ordinal);
    }

    [Fact]
    public void RegionToClipmapCompute_UsesIndex3d_XZ_YLinearOrder()
    {
        string src = ReadRepoFileOrSkip(Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "lumonscene_trace_scene_region_to_clipmap.csh"));

        // Expect: index3d = x | (z << 5) | (y << 10) => linear = (y*32 + z)*32 + x
        Assert.Contains("(local.y * VGE_REGION_SIZE + local.z) * VGE_REGION_SIZE + local.x", src, StringComparison.Ordinal);
        Assert.DoesNotContain("(local.z * VGE_REGION_SIZE + local.y) * VGE_REGION_SIZE + local.x", src, StringComparison.Ordinal);
    }
}
