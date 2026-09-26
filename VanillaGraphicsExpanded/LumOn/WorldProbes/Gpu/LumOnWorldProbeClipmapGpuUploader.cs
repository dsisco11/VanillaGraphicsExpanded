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
internal sealed class LumOnWorldProbeClipmapGpuUploader : IDisposable
{
    private static readonly GlPipelineDesc ResolvePso = new(
        defaultMask: default(GlPipelineStateMask)
            .With(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.BlendEnable)
            .With(GlPipelineStateId.CullFaceEnable),
        nonDefaultMask: default);

    private readonly ICoreClientAPI capi;

    private readonly GpuVao probeVao;
    private readonly GpuVbo probeVbo;

    private readonly GpuVao tileVao;
    private readonly GpuVbo tileVbo;

    private readonly List<ProbeResolveVertex> probeVertices = new();
    private readonly List<TileResolveVertex> tileVertices = new();
    private bool isDisposed;
    private WorldProbeHybridCommit? hybridCommit;

    public LumOnWorldProbeClipmapGpuUploader(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));

        probeVao = GpuVao.Create(debugName: "VGE_WorldProbeProbeResolve_VAO");
        probeVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, debugName: "VGE_WorldProbeProbeResolve_VBO");

        using (var vaoScope = probeVao.BindScope())
        using (var vboScope = probeVbo.BindScope())
        {
            int stride = Marshal.SizeOf<ProbeResolveVertex>();

            // vec2 atlasCoord
            probeVao.EnableAttrib(0);
            probeVao.AttribPointer(0, 2, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec3 aoDirWorld
            probeVao.EnableAttrib(1);
            probeVao.AttribPointer(1, 3, VertexAttribPointerType.Float, normalized: false, stride, 8);

            // float aoConfidence
            probeVao.EnableAttrib(2);
            probeVao.AttribPointer(2, 1, VertexAttribPointerType.Float, normalized: false, stride, 20);

            // float confidence
            probeVao.EnableAttrib(3);
            probeVao.AttribPointer(3, 1, VertexAttribPointerType.Float, normalized: false, stride, 24);

            // float meanLogHitDistance
            probeVao.EnableAttrib(4);
            probeVao.AttribPointer(4, 1, VertexAttribPointerType.Float, normalized: false, stride, 28);

            // float skyIntensity
            probeVao.EnableAttrib(5);
            probeVao.AttribPointer(5, 1, VertexAttribPointerType.Float, normalized: false, stride, 32);

            // uint flags (integer attribute)
            probeVao.EnableAttrib(6);
            probeVao.AttribIPointer(6, 1, VertexAttribIntegerType.UnsignedInt, stride, 36);
        }

        tileVao = GpuVao.Create(debugName: "VGE_WorldProbeTileResolve_VAO");
        tileVbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, debugName: "VGE_WorldProbeTileResolve_VBO");

        using (var vaoScope = tileVao.BindScope())
        using (var vboScope = tileVbo.BindScope())
        {
            int stride = Marshal.SizeOf<TileResolveVertex>();

            // vec2 atlasCoord
            tileVao.EnableAttrib(0);
            tileVao.AttribPointer(0, 2, VertexAttribPointerType.Float, normalized: false, stride, 0);

            // vec4 radianceRGBA (alpha is signed log distance)
            tileVao.EnableAttrib(1);
            tileVao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 8);
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
        using var fixedFunctionState = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        GlStateCache.Current.InvalidateAll();
        GlStateCache.Current.Apply(ResolvePso);

        // Pass 1: tile samples -> radiance atlas
        if (tileProg is not null && !tileProg.LoadError && !tileProg.Disposed && tileVertices.Count > 0)
        {
            using (GlGpuProfiler.Instance.Scope(tileProg.PassName))
            {
                tileProg.Use();
                tileProg.AtlasSize = new Vec2f(resources.RadianceAtlasWidth, resources.RadianceAtlasHeight);

                using var vaoScope = tileVao.BindScope();

                var rfbo = resources.GetRadianceFbo();
                rfbo.Bind();
                GL.Viewport(0, 0, resources.RadianceAtlasWidth, resources.RadianceAtlasHeight);

                ReadOnlySpan<TileResolveVertex> tileData = CollectionsMarshal.AsSpan(tileVertices);
                tileVbo.UploadData(tileData);

                GL.DrawArrays(PrimitiveType.Points, 0, tileData.Length);
                Rendering.GpuFramebuffer.Unbind();
                tileProg.Stop();
            }
        }

        // Pass 2: per-probe scalars -> vis/dist/meta atlases
        using (GlGpuProfiler.Instance.Scope(probeProg.PassName))
        {
            probeProg.Use();
            probeProg.AtlasSize = new Vec2f(resources.AtlasWidth, resources.AtlasHeight);

            using (var vaoScope = probeVao.BindScope())
            {
                var fbo = resources.GetFbo();
                fbo.Bind();
                GL.Viewport(0, 0, resources.AtlasWidth, resources.AtlasHeight);

                ReadOnlySpan<ProbeResolveVertex> probeData = CollectionsMarshal.AsSpan(probeVertices);
                probeVbo.UploadData(probeData);

                GL.DrawArrays(PrimitiveType.Points, 0, probeData.Length);
                Rendering.GpuFramebuffer.Unbind();
            }

            probeProg.Stop();
        }

        return usedProbes;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        hybridCommit?.Dispose(); hybridCommit = null;

        probeVbo.Dispose();
        probeVao.Dispose();

        tileVbo.Dispose();
        tileVao.Dispose();
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
