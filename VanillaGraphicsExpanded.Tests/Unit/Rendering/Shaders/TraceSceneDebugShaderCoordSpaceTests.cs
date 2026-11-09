using System.IO;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks authored trace-scene shader contracts against build-captured source inputs.</summary>
public sealed class TraceSceneDebugShaderCoordSpaceTests
{
    #region Public API
    #region Frame conversion contracts
    /// <summary>The trace-scene cell shader source contains the shared frame conversion token.</summary>
    [Fact]
    public void TraceSceneCellSourceContainsConversionToken()
    {
        // Arrange the trace-scene shader path within the immutable build input set.
        string path = Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes", "debug", "trace_scene_debug_surface_cell.glsl");

        // Act by reading the authored source from the test-build snapshot.
        string src = ReadBuiltShaderText(path);

        // Assert the authored source contains the shared conversion routine token.
        Assert.Contains("VgeFrameMatrixSpaceToWorldCell", src, StringComparison.Ordinal);
    }

    /// <summary>The scenes overview shader source contains the trace-scene cell helper token.</summary>
    [Fact]
    public void ScenesOverviewSourceContainsTraceCellHelperToken()
    {
        // Arrange the scenes-overview shader path within the immutable build input set.
        string path = Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "lumon_debug_view_lum_on_scenes_overview.fsh");

        // Act by reading the authored source from the test-build snapshot.
        string src = ReadBuiltShaderText(path);

        // Assert the authored source contains the trace-scene cell helper token.
        Assert.Contains("traceSceneDebugSurfaceCell", src, StringComparison.Ordinal);
    }
    #endregion

    #region Source fragment checks
    /// <summary>The surface-cache source contains a capture flag token and omits one combined literal.</summary>
    [Fact]
    public void SurfaceCacheSourceContainsCaptureFlagAndNotCombinedLiteral()
    {
        // Arrange the surface-cache shader path within the immutable build input set.
        string path = Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "includes",
            "lumonscene_surface_cache.glsl");

        // Act by reading the authored source from the test-build snapshot.
        string src = ReadBuiltShaderText(path);

        // Assert the source contains the capture flag token and lacks this exact combined literal.
        Assert.Contains("VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE", src, StringComparison.Ordinal);
        Assert.DoesNotContain("VGE_LUMONSCENE_FLAG_NEEDS_CAPTURE | VGE_LUMONSCENE_FLAG_NEEDS_RELIGHT", src, StringComparison.Ordinal);
    }

    /// <summary>The relight source contains exact batch-index and hash-call fragments.</summary>
    [Fact]
    public void RelightShaderSourceContainsBatchIndexAndHashLiterals()
    {
        // Arrange the voxel-relight shader path within the immutable build input set.
        string path = Path.Combine(
            "VanillaGraphicsExpanded",
            "assets",
            "vanillagraphicsexpanded",
            "shaders",
            "lumonscene_relight_voxel_dda.csh");

        // Act by reading the authored source from the test-build snapshot.
        string src = ReadBuiltShaderText(path);

        // Assert the source contains the two exact fragments; this does not connect them semantically.
        Assert.Contains("uint batchIndexIn = w.z", src, StringComparison.Ordinal);
        Assert.Contains("Squirrel3HashU(physicalPageId, virtualPageIndex, linear)", src, StringComparison.Ordinal);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reads an authored shader file from the immutable inputs captured for this test build.</summary>
    private static string ReadBuiltShaderText(string relativePath)
    {
        const string prefix = "VanillaGraphicsExpanded/assets/vanillagraphicsexpanded/";
        string normalized = relativePath.Replace('\\', '/');
        Assert.StartsWith(prefix, normalized);
        return TestAssetSnapshot.Built.Text(normalized[prefix.Length..]);
    }
    #endregion
}
