using System.Numerics;
using System.Reflection;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Synchronizes matched direct-only producer work and retained directional consumers without using final image values as readiness.</summary>
internal static class SurfaceLightingRefreshSynchronization
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    #region Producer completion
    /// <summary>Completes captures and the current direct refresh sweep while excluding indirect bounce work from every compared image.</summary>
    public static void RefreshAndFreeze(SurfaceLightingConsumerRuntimeFixture runtime)
    {
        var config = runtime.Cache.Config.LumOn.LumonScene;
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = config.RelightIndirectPagesPerFrame = 0;
        config.RelightTexelsPerPagePerFrame = 4096;
        // Process changed source/geometry inputs before accepting previously captured identities.
        runtime.Frame();
        runtime.RunUntil(runtime.Cache.AllRequestedCaptured);
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = 256;
        runtime.Frame();
        var producer = Read<object>(runtime.Cache, "relight");
        runtime.RunUntil(() => runtime.Cache.AllRequestedCaptured() && runtime.Cache.AllRequestedLightingReady() &&
            Read<long>(producer, "directInvalidation") == runtime.Cache.Geometry.Resources!.InvalidationRevision &&
            Read<Dictionary<uint, int>>(producer, "changedDirectPages").Count == 0);
        config.RelightSeedPagesPerFrame = config.RelightDirectPagesPerFrame = 0;
    }
    #endregion

    #region Consumer completion
    /// <summary>Waits for new successful full-tile world publications and a fixed point in the retained screen directional history.</summary>
    public static void CompleteConsumers(SurfaceLightingConsumerRuntimeFixture runtime, SpatialLightingScene scene,
        int maximumFrames = SurfaceLightingConsumerRuntimeFixture.FrameBudget)
    {
        int firstFrame = runtime.Cache.Frames;
        var scheduler = Read<object>(runtime.WorldRenderer, "scheduler");
        var level = Read<Array>(scheduler, "levels").GetValue(0)!;
        var updated = Read<int[]>(level, "lastUpdatedFrame");
        Assert.True(runtime.WorldBuffers.TryGetRuntimeParams(out var player, out _, out float spacing,
            out _, out int resolution, out var origins, out var rings));
        var origin = origins[0] + new Vector3((float)player.X, (float)player.Y, (float)player.Z);
        var slots = new List<int>();
        for (int z = 0; z < resolution; z++) for (int y = 0; y < resolution; y++) for (int x = 0; x < resolution; x++)
        {
            var center = origin + (new Vector3(x, y, z) + new Vector3(.5f)) * spacing;
            if (center.X < scene.RoomOrigin + 1 || center.X >= scene.RoomOrigin + 7 ||
                scene.Solid((int)MathF.Floor(center.X), (int)MathF.Floor(center.Y), (int)MathF.Floor(center.Z))) continue;
            int sx = (x + (int)rings[0].X) % resolution, sy = (y + (int)rings[0].Y) % resolution,
                sz = (z + (int)rings[0].Z) % resolution;
            slots.Add(sx + sy * resolution + sz * resolution * resolution);
        }
        Assert.NotEmpty(slots);
        Assert.Equal(runtime.WorldBuffers.Resources!.WorldProbeTileSize * runtime.WorldBuffers.Resources.WorldProbeTileSize,
            runtime.Cache.Config.WorldProbeClipmap.AtlasTexelsPerUpdate);
        // Cover the fixture's whole finite grid per admission round so unsupported neighbors
        // cannot consume every slot ahead of valid probes awaiting a source refresh.
        var config = runtime.Cache.Config.WorldProbeClipmap;
        int oldTrace = config.TraceMaxProbesPerFrame, oldUpload = config.UploadBudgetBytesPerFrame;
        int[] oldLevels = config.PerLevelProbeUpdateBudget;
        int probes = resolution * resolution * resolution;
        config.TraceMaxProbesPerFrame = probes;
        config.PerLevelProbeUpdateBudget = [probes];
        // Charge the GPU completion descriptor and every directional sample, matching the uploader's bounded layout.
        config.UploadBudgetBytesPerFrame = probes * (48 + (config.AtlasTexelsPerUpdate << 5));
        var previous = slots.Select(slot => updated[slot]).ToArray();
        var completions = new int[slots.Count];
        // The first completion may belong to a pre-refresh admission. A second full-tile
        // success must have been admitted after that result retired, against frozen lighting.
        try
        {
            runtime.RunUntil(() =>
            {
                for (int i = 0; i < slots.Count; i++)
                    if (updated[slots[i]] > previous[i]) { previous[i] = updated[slots[i]]; completions[i]++; }
                return completions.All(count => count >= 2);
            }, maximumFrames);
        }
        finally
        {
            config.TraceMaxProbesPerFrame = oldTrace;
            config.PerLevelProbeUpdateBudget = oldLevels;
            config.UploadBudgetBytesPerFrame = oldUpload;
        }
        int tile = VanillaGraphicsExpanded.Rendering.DynamicTexture3D.OctahedralSize;
        int sweep = (tile * tile + runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame - 1) /
            runtime.Cache.Config.LumOn.ProbeAtlasTexelsPerFrame;
        float[]? previousHistory = null;
        float[]? previousMeta = null;
        int sampledFrame = runtime.Cache.Frames;
        bool stable = false;
        // Reserve the remainder of the existing transition budget for complete screen sweeps;
        // neither producer brightness nor the desired final image is a completion predicate.
        runtime.RunUntil(() =>
        {
            if (stable) return true;
            if (runtime.Cache.Frames - sampledFrame < sweep) return false;
            sampledFrame = runtime.Cache.Frames;
            var current = runtime.Screen.ScreenProbeAtlasHistoryTex!.ReadPixels();
            var meta = runtime.Screen.ScreenProbeAtlasMetaHistoryTex!.ReadPixels();
            stable = previousHistory != null && current.AsSpan().SequenceEqual(previousHistory) &&
                meta.AsSpan().SequenceEqual(previousMeta);
            previousHistory = current;
            previousMeta = meta;
            return stable;
        }, maximumFrames - (runtime.Cache.Frames - firstFrame));
    }

    /// <summary>Observes existing private lifecycle data without mutating renderer state or exposing a production test API.</summary>
    private static T Read<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, PrivateInstance)?.GetValue(owner)
            ?? throw new InvalidOperationException($"Missing synchronization field {owner.GetType().Name}.{name}."));
    #endregion
}
