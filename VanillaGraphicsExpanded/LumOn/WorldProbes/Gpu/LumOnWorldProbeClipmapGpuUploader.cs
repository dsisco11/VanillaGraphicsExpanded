using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns reusable render-thread staging buffers for immediate probe atlas uploads.</summary>
internal sealed partial class LumOnWorldProbeClipmapGpuUploader : IDisposable
{
    private readonly ICoreClientAPI capi;

    private readonly ArrayGraphicsGeometry probeGeometry;
    private readonly GpuVbo probeVbo;

    private readonly ArrayGraphicsGeometry tileGeometry;
    private readonly GpuVbo tileVbo;

    private readonly List<ProbeResolveVertex> probeVertices = new();
    private readonly List<TileResolveVertex> tileVertices = new();
    private bool isDisposed;
    private WorldProbeHybridCommit? hybridCommit;

    /// <summary>Creates streaming buffers and privately configured geometry, retiring partial allocation on failure.</summary>
    public LumOnWorldProbeClipmapGpuUploader(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));

        probeVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, debugName: "VGE_WorldProbeProbeResolve_VBO");

        try
        {
            probeGeometry = new(ProbeLayout, PrimitiveType.Points, new Dictionary<int, GpuVbo> { [0] = probeVbo });
            tileVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, debugName: "VGE_WorldProbeTileResolve_VBO");
            tileGeometry = new(TileLayout, PrimitiveType.Points, new Dictionary<int, GpuVbo> { [0] = tileVbo });
        }
        catch
        {
            tileVbo?.Dispose();
            probeGeometry?.Dispose();
            probeVbo.Dispose();
            throw;
        }
    }

    /// <summary>Uploads borrowed results synchronously; neither input spans nor staging views escape this call.</summary>
    public int Upload(LumOnWorldProbeClipmapGpuResources resources,
        ReadOnlySpan<LumOnWorldProbeTraceResult> results, int uploadBudgetBytesPerFrame)
    {
        int budget = uploadBudgetBytesPerFrame > 0 ? uploadBudgetBytesPerFrame : int.MaxValue;
        int committed = 0;
        foreach (ref readonly var result in results)
        {
            // Lighting-only retries no longer own resident RGB, but still belong to the original trace.
            try { if (result.GpuIdentity != null && !result.GpuIdentity.IsCurrent) continue; }
            catch (Exception error)
            { capi.Logger.Error("[VGE] World-probe commit identity failed: {0}", error.Message); continue; }
            int bytes = GetUploadBytes(result);
            if (bytes > budget) break;
            if (result.GpuLease == null)
            {
                bool missingOwner = false;
                foreach (var sample in result.AtlasSamples.AsSpan()) missingOwner |= sample.GpuRayIndex >= 0;
                if (missingOwner) continue;
                committed += UploadCpu(resources, MemoryMarshal.CreateReadOnlySpan(in result, 1), bytes);
            }
            else
            {
                // Prepare every resource before publication; failure cannot expose metadata alone.
                try
                {
                    if (!result.GpuLease.IsCurrent) continue;
                    hybridCommit ??= new WorldProbeHybridCommit(capi);
                    if (!hybridCommit.Commit(resources, result)) continue;
                    committed++;
                }
                catch (Exception error)
                { capi.Logger.Error("[VGE] World-probe hybrid commit failed: {0}", error.Message); continue; }
            }
            budget -= bytes;
        }
        return committed;
    }

    /// <summary>Charges actual staging bytes for resident commits or compatibility raster uploads.</summary>
    public static int GetUploadBytes(in LumOnWorldProbeTraceResult result) => result.GpuLease != null
        ? 48 + (result.AtlasSamples.AsSpan().Length << 5) : 40 + 24 * result.AtlasSamples.AsSpan().Length;

    /// <summary>Publishes CPU-only directional values through the existing raster resolve programs.</summary>
    private int UploadCpu(
        LumOnWorldProbeClipmapGpuResources resources,
        ReadOnlySpan<LumOnWorldProbeTraceResult> results,
        int uploadBudgetBytesPerFrame)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));


        var probeProg = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<LumOnWorldProbeClipmapResolveShaderProgram>(capi, "lumon_worldprobe_clipmap_resolve");
        if (probeProg is null || !probeProg.EnsureReady())
        {
            return 0;
        }

        var tileProg = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<LumOnWorldProbeRadianceTileResolveShaderProgram>(capi, "lumon_worldprobe_radiance_tile_resolve");

        int bytesPerProbeVertex = Marshal.SizeOf<ProbeResolveVertex>();
        int bytesPerTileVertex = Marshal.SizeOf<TileResolveVertex>();

        // Publish a probe's metadata only when every admitted directional sample can be uploaded.
        if (tileProg is null || !tileProg.EnsureReady()) return 0;
        int maxProbes = results.Length;
        int maxTileVertices = int.MaxValue;
        int remainingBytes = uploadBudgetBytesPerFrame > 0 ? uploadBudgetBytesPerFrame : int.MaxValue;
        int usedProbes = 0;
        probeVertices.Clear();
        tileVertices.Clear();

        for (int i = 0; i < results.Length && usedProbes < maxProbes; i++)
        {
            var r = results[i];
            int level = r.Request.Level;
            if ((uint)level >= (uint)resources.Levels) continue;

            int bytes = bytesPerProbeVertex + bytesPerTileVertex * r.AtlasSamples.AsSpan().Length;
            if (bytes > remainingBytes) break;
            remainingBytes -= bytes;
            Vec3i storage = r.Request.StorageIndex;

            int u = storage.X + storage.Z * resources.Resolution;
            int v = storage.Y + level * resources.AtlasHeightPerLevel;

            uint flags = LumOnWorldProbeMetaFlags.Valid;
            if (r.MeanLogHitDistance <= 0f && r.ShortRangeAoConfidence > 0.99f)
            {
                flags |= LumOnWorldProbeMetaFlags.SkyOnly;
            }

            probeVertices.Add(ProbeResolveVertex.From(r, u, v, flags));
            usedProbes++;

            if (tileProg is null || !tileProg.EnsureReady())
            {
                continue;
            }

            if (r.AtlasSamples.IsDefaultOrEmpty)
            {
                continue;
            }

            var (tileU0, tileV0) = LumOnWorldProbeLayout.GetRadianceAtlasTileOrigin(
                storageX: storage.X,
                storageY: storage.Y,
                storageZ: storage.Z,
                level: level,
                resolution: resources.Resolution,
                tileSize: resources.WorldProbeTileSize);

            for (int sIdx = 0; sIdx < r.AtlasSamples.Length && tileVertices.Count < maxTileVertices; sIdx++)
            {
                var s = r.AtlasSamples[sIdx];

                int tu = tileU0 + s.OctX;
                int tv = tileV0 + s.OctY;

                tileVertices.Add(TileResolveVertex.From(tu, tv, s.RadianceRgb, s.AlphaEncodedDistSigned));
            }
        }

        if (probeVertices.Count == 0)
        {
            return 0;
        }

        using var gpuScope = GlGpuProfiler.Instance.Scope("LumOn.WorldProbe.UploadResolve");
        var radianceTarget = resources.GetRadianceFbo();
        var metadataTarget = resources.GetFbo();
        var tileState = PreparePipeline(ref tilePipeline, tileProg, TileLayout, radianceTarget, TileOutputs);
        var probeState = PreparePipeline(ref probePipeline, probeProg, ProbeLayout, metadataTarget, ProbeOutputs);
        bool submitted = GraphicsCommandContext.TryRun("LumOn.WorldProbe.UploadResolve", [tileState, probeState], true, commands =>
        {
            // Publish radiance first; metadata becomes visible only after both sequential passes succeed.
            if (tileVertices.Count > 0)
            {
                tileProg.AtlasSize = new Vec2f(resources.RadianceAtlasWidth, resources.RadianceAtlasHeight);
                tileVbo.UploadData((ReadOnlySpan<TileResolveVertex>)CollectionsMarshal.AsSpan(tileVertices));
                Submit(commands, tileState, tileGeometry, radianceTarget, TileOutputs, tileVertices.Count);
            }
            probeProg.AtlasSize = new Vec2f(resources.AtlasWidth, resources.AtlasHeight);
            probeVbo.UploadData((ReadOnlySpan<ProbeResolveVertex>)CollectionsMarshal.AsSpan(probeVertices));
            Submit(commands, probeState, probeGeometry, metadataTarget, ProbeOutputs, probeVertices.Count);
        });
        if (!submitted) return 0;
        return usedProbes;
    }

    /// <summary>Retires geometry before its borrowed streams and invalidates renderer-owned realizations.</summary>
    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        hybridCommit?.Dispose(); hybridCommit = null;

        probeGeometry.Dispose();
        probeVbo.Dispose();
        probePipeline?.Dispose();

        tileGeometry.Dispose();
        tileVbo.Dispose();
        tilePipeline?.Dispose();
        pipelineLifetime.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ProbeResolveVertex
    {
        public float AtlasU;
        public float AtlasV;

        public float AoDirX;
        public float AoDirY;
        public float AoDirZ;

        public float AoConfidence;
        public float Confidence;
        public float MeanLogHitDistance;

        public float SkyIntensity;

        public uint Flags;

        public static ProbeResolveVertex From(in LumOnWorldProbeTraceResult r, int atlasU, int atlasV, uint flags)
        {
            return new ProbeResolveVertex
            {
                AtlasU = atlasU,
                AtlasV = atlasV,

                AoDirX = r.ShortRangeAoDirWorld.X,
                AoDirY = r.ShortRangeAoDirWorld.Y,
                AoDirZ = r.ShortRangeAoDirWorld.Z,

                AoConfidence = r.ShortRangeAoConfidence,
                Confidence = r.Confidence,
                MeanLogHitDistance = r.MeanLogHitDistance,

                SkyIntensity = r.SkyIntensity,

                Flags = flags,
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TileResolveVertex
    {
        public float AtlasU;
        public float AtlasV;

        public float RadianceR;
        public float RadianceG;
        public float RadianceB;
        public float RadianceA;

        public static TileResolveVertex From(int atlasU, int atlasV, Vector3 radianceRgb, float alphaSigned)
        {
            return new TileResolveVertex
            {
                AtlasU = atlasU,
                AtlasV = atlasV,
                RadianceR = radianceRgb.X,
                RadianceG = radianceRgb.Y,
                RadianceB = radianceRgb.Z,
                RadianceA = alphaSigned,
            };
        }
    }
}
