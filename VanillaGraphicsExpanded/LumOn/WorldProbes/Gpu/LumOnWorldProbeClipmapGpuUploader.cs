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

internal sealed class LumOnWorldProbeClipmapGpuUploader : IDisposable
{
    private readonly ICoreClientAPI capi;

    private readonly GpuVao probeVao;
    private readonly GpuVbo probeVbo;

    private readonly GpuVao tileVao;
    private readonly GpuVbo tileVbo;

    private bool isDisposed;

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

    public int Upload(
        LumOnWorldProbeClipmapGpuResources resources,
        IReadOnlyList<LumOnWorldProbeTraceResult> results,
        int uploadBudgetBytesPerFrame)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (results is null) throw new ArgumentNullException(nameof(results));

        var probeProg = capi.Shader.GetProgramByName("lumon_worldprobe_clipmap_resolve") as LumOnWorldProbeClipmapResolveShaderProgram;
        if (probeProg is null || probeProg.LoadError || probeProg.Disposed)
        {
            return 0;
        }

        var tileProg = capi.Shader.GetProgramByName("lumon_worldprobe_radiance_tile_resolve") as LumOnWorldProbeRadianceTileResolveShaderProgram;

        int bytesPerProbeVertex = Marshal.SizeOf<ProbeResolveVertex>();
        int bytesPerTileVertex = Marshal.SizeOf<TileResolveVertex>();

        int maxProbes = results.Count;
        int maxTileVertices = int.MaxValue;
        if (uploadBudgetBytesPerFrame > 0)
        {
            maxProbes = Math.Min(maxProbes, Math.Max(0, uploadBudgetBytesPerFrame / Math.Max(1, bytesPerProbeVertex)));
            int usedProbeBytes = maxProbes * bytesPerProbeVertex;
            int remaining = Math.Max(0, uploadBudgetBytesPerFrame - usedProbeBytes);
            maxTileVertices = bytesPerTileVertex <= 0 ? int.MaxValue : remaining / Math.Max(1, bytesPerTileVertex);
        }

        int usedProbes = 0;
        var probeVertices = new List<ProbeResolveVertex>(capacity: maxProbes);
        var tileVertices = new List<TileResolveVertex>(capacity: Math.Min(4096, maxTileVertices));

        for (int i = 0; i < results.Count && usedProbes < maxProbes; i++)
        {
            var r = results[i];
            int level = r.Request.Level;
            if ((uint)level >= (uint)resources.Levels) continue;

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

            if (tileProg is null || tileProg.LoadError || tileProg.Disposed)
            {
                continue;
            }

            if (r.AtlasSamples is null || r.AtlasSamples.Length == 0)
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

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        // Pass 1: tile samples -> radiance atlas
        if (tileProg is not null && !tileProg.LoadError && !tileProg.Disposed && tileVertices.Count > 0)
        {
            tileProg.Use();
            tileProg.AtlasSize = new Vec2f(resources.RadianceAtlasWidth, resources.RadianceAtlasHeight);

            using var vaoScope = tileVao.BindScope();

            var rfbo = resources.GetRadianceFbo();
            rfbo.Bind();
            GL.Viewport(0, 0, resources.RadianceAtlasWidth, resources.RadianceAtlasHeight);

            TileResolveVertex[] tileData = tileVertices.ToArray();
            tileVbo.UploadData(tileData);

            GL.DrawArrays(PrimitiveType.Points, 0, tileData.Length);
            Rendering.GpuFramebuffer.Unbind();
            tileProg.Stop();
        }

        // Pass 2: per-probe scalars -> vis/dist/meta atlases
        probeProg.Use();
        probeProg.AtlasSize = new Vec2f(resources.AtlasWidth, resources.AtlasHeight);

        using (var vaoScope = probeVao.BindScope())
        {
            var fbo = resources.GetFbo();
            fbo.Bind();
            GL.Viewport(0, 0, resources.AtlasWidth, resources.AtlasHeight);

            ProbeResolveVertex[] probeData = probeVertices.ToArray();
            probeVbo.UploadData(probeData);

            GL.DrawArrays(PrimitiveType.Points, 0, probeData.Length);
            Rendering.GpuFramebuffer.Unbind();
        }

        probeProg.Stop();

        return usedProbes;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

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
