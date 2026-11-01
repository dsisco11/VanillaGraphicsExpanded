using System.Reflection;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes completed work and requested lighting tiles without treating retained validity as freshness.</summary>
internal sealed partial class SurfaceCacheRuntimeFixture
{
    private const BindingFlags LightingFields = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Captures the produced energy and completed work needed to distinguish decay from stalled retained history.</summary>
    internal readonly record struct LightingProgress(int Pages, int TileSize, float Direct, float Indirect,
        float Outgoing, float IndirectWeight, long Generation, long IndirectCompletions);

    #region Completed work observations
    /// <summary>Counts successful indirect page batches without reading any texture data.</summary>
    public long CompletedIndirectPages =>
        (long)typeof(LumonSceneRelightUpdateRenderer).GetField("diagnosticIndirectAttempts", LightingFields)!.GetValue(relight)! -
        (long)typeof(LumonSceneRelightUpdateRenderer).GetField("diagnosticIndirectFailures", LightingFields)!.GetValue(relight)!;

    /// <summary>Requires capture, initialized requested texels, and completion of the current direct refresh sweep.</summary>
    public bool RequestedDirectRefreshComplete() => AllRequestedCaptured() && AllRequestedLightingReady() &&
        (long)typeof(LumonSceneRelightUpdateRenderer).GetField("directInvalidation", LightingFields)!.GetValue(relight)! == Geometry.Resources!.InvalidationRevision &&
        ((Dictionary<uint, int>)typeof(LumonSceneRelightUpdateRenderer).GetField("changedDirectPages", LightingFields)!.GetValue(relight)!).Count == 0;

    /// <summary>Reduces every requested physical tile on the GPU, excluding unused atlas storage.</summary>
    public LightingProgress ObserveRequestedLighting()
    {
        Assert.True(TryGetLighting(out var snapshot));
        Assert.True(Feedback.TryGetNearDispatchState(out _, out _, out var mapping, out _));
        Assert.True(TryGetRequestedPages(mapping, out var pages));
        // These stationary tests author exactly one requested room. This prevents a
        // whole-room service assertion from overlooking additional resident candidates.
        Assert.Equal(pages.Count, mapping.Count);
        float direct = 0, indirect = 0, outgoing = 0, weight = 0;
        // A local region plan is shared across the three textures from this one publication.
        // Nothing is retained across callbacks, frames, writes or resource replacement.
        var regions = SurfaceCacheReadiness.Plan(pages.Select(page => page.Physical),
            snapshot.TileSize, snapshot.TilesPerAxis, snapshot.TilesPerAtlas);
        foreach (var region in regions)
        {
            direct = Math.Max(direct, Peak(snapshot.DirectIrradiance, region, out _));
            indirect = Math.Max(indirect, Peak(snapshot.IndirectIrradiance, region, out float samples));
            outgoing = Math.Max(outgoing, Peak(snapshot.OutgoingRadiance, region, out _));
            weight = Math.Max(weight, samples);
        }
        return new(pages.Count, snapshot.TileSize, direct, indirect, outgoing, weight, snapshot.Generation, CompletedIndirectPages);

        /// <summary>Checks every finite RGB value and retains the largest history weight separately from energy.</summary>
        float Peak(Texture3D texture, SurfaceCacheReadiness.Region region, out float maximumWeight)
        {
            var selection = new RHITextureRegion(new(0, region.Layer), region.X, region.Y, 0, region.Width, region.Height);
            var rgb = ImageAssertions.Inspect(texture, selection: selection, channelMask: 7);
            var alpha = ImageAssertions.Inspect(texture, selection: selection, channelMask: 8);
            rgb.AssertFiniteRange(); alpha.AssertFiniteRange();
            maximumWeight = (float)Math.Max(0, alpha.Maximum);
            return (float)(Math.Max(Math.Abs(rgb.Minimum), Math.Abs(rgb.Maximum)) / LightingStorage.PersistentScale);
        }
    }
    #endregion
}
