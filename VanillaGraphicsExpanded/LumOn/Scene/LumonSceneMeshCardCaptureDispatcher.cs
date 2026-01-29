using System;
using System.Buffers;
using System.Numerics;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.11 mesh-card capture dispatcher (GL 4.3 compute).
/// Produces per-texel depth + normal in the physical atlases for a set of card pages.
/// </summary>
internal sealed class LumonSceneMeshCardCaptureDispatcher : IDisposable
{
    private readonly ICoreClientAPI capi;

    private GpuComputePipeline? captureMeshCardPipeline;

    private readonly LumonSceneWorkQueueGpu<LumonSceneMeshCardCaptureWorkGpu> meshCardCaptureWork;
    private GpuShaderStorageBuffer? trianglesSsbo;
    private int trianglesCapacityBytes;

    public LumonSceneMeshCardCaptureDispatcher(ICoreClientAPI capi, int maxCaptureJobs = 256)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        meshCardCaptureWork = new LumonSceneWorkQueueGpu<LumonSceneMeshCardCaptureWorkGpu>(
            debugName: "LumOn.LumonScene.MeshCardCaptureWork",
            capacityItems: Math.Max(1, maxCaptureJobs));
    }

    public void Dispose()
    {
        captureMeshCardPipeline?.Dispose();
        captureMeshCardPipeline = null;

        meshCardCaptureWork.Dispose();

        trianglesSsbo?.Dispose();
        trianglesSsbo = null;
        trianglesCapacityBytes = 0;
    }

    public void Dispatch(
        LumonSceneFieldGpuResources fieldGpu,
        LumonScenePhysicalAtlasGpuResources atlases,
        ReadOnlySpan<LumonSceneMeshCardCaptureJob> jobs,
        ReadOnlySpan<LumonSceneMeshCardTriangleGpu> triangles,
        int tileSizeTexels,
        int tilesPerAxis,
        int tilesPerAtlas,
        float captureDepthRange)
    {
        if (jobs.Length <= 0)
        {
            return;
        }

        if (triangles.Length <= 0)
        {
            return;
        }

        if (fieldGpu is null) throw new ArgumentNullException(nameof(fieldGpu));
        if (atlases is null) throw new ArgumentNullException(nameof(atlases));

        if (!EnsureCaptureMeshCardPipeline())
        {
            return;
        }

        // Upload triangles (shared across jobs).
        EnsureTrianglesBufferCreated();
        UploadTriangles(triangles);

        // Upload per-job work + per-physicalPageId patch metadata.
        LumonSceneMeshCardCaptureWorkGpu[] workScratch = ArrayPool<LumonSceneMeshCardCaptureWorkGpu>.Shared.Rent(jobs.Length);
        try
        {
            for (int i = 0; i < jobs.Length; i++)
            {
                var job = jobs[i];
                workScratch[i] = new LumonSceneMeshCardCaptureWorkGpu(
                    physicalPageId: job.PhysicalPageId,
                    triangleOffset: job.TriangleOffset,
                    triangleCount: job.TriangleCount);

                UploadPatchMetadata(fieldGpu, job);
            }

            meshCardCaptureWork.ResetAndUpload(workScratch.AsSpan(0, jobs.Length));

            using (captureMeshCardPipeline!.UseScope())
            {
                // SSBO bindings.
                meshCardCaptureWork.Items.BindBase(bindingIndex: 0);
                fieldGpu.PatchMetadata.Ssbo.BindBase(bindingIndex: 1);
                trianglesSsbo!.BindBase(bindingIndex: 2);

                // Bind outputs as layered images.
                _ = captureMeshCardPipeline.ProgramLayout.TryBindImageTexture(
                    imageUniformName: "vge_depthAtlas",
                    texture: atlases.DepthAtlas,
                    access: TextureAccess.WriteOnly,
                    level: 0,
                    layered: true,
                    layer: 0,
                    formatOverride: SizedInternalFormat.R16f);

                _ = captureMeshCardPipeline.ProgramLayout.TryBindImageTexture(
                    imageUniformName: "vge_materialAtlas",
                    texture: atlases.MaterialAtlas,
                    access: TextureAccess.WriteOnly,
                    level: 0,
                    layered: true,
                    layer: 0,
                    formatOverride: SizedInternalFormat.Rgba8);

                _ = captureMeshCardPipeline.TrySetUniform1("vge_tileSizeTexels", (uint)Math.Max(1, tileSizeTexels));
                _ = captureMeshCardPipeline.TrySetUniform1("vge_tilesPerAxis", (uint)Math.Max(1, tilesPerAxis));
                _ = captureMeshCardPipeline.TrySetUniform1("vge_tilesPerAtlas", (uint)Math.Max(1, tilesPerAtlas));
                _ = captureMeshCardPipeline.TrySetUniform1("vge_borderTexels", 0u);
                _ = captureMeshCardPipeline.TrySetUniform1("vge_captureDepthRange", Math.Max(1e-6f, captureDepthRange));

                int gx = (Math.Max(1, tileSizeTexels) + 7) / 8;
                int gy = (Math.Max(1, tileSizeTexels) + 7) / 8;
                GL.DispatchCompute(gx, gy, jobs.Length);
            }
        }
        finally
        {
            ArrayPool<LumonSceneMeshCardCaptureWorkGpu>.Shared.Return(workScratch, clearArray: false);
        }
    }

    private bool EnsureCaptureMeshCardPipeline()
    {
        if (captureMeshCardPipeline is not null && captureMeshCardPipeline.IsValid)
        {
            return true;
        }

        captureMeshCardPipeline?.Dispose();
        captureMeshCardPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_capture_meshcard.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene mesh-card capture shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_capture_meshcard",
            glslSource: src,
            pipeline: out captureMeshCardPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.CaptureMeshCard",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene capture mesh-card compute shader: {0}", infoLog);
            captureMeshCardPipeline = null;
            return false;
        }

        return captureMeshCardPipeline is not null;
    }

    private void EnsureTrianglesBufferCreated()
    {
        trianglesSsbo ??= GpuShaderStorageBuffer.Create(
            BufferUsageHint.DynamicDraw,
            debugName: "LumOn.LumonScene.MeshCardTriangles(SSBO)");
    }

    private void UploadTriangles(ReadOnlySpan<LumonSceneMeshCardTriangleGpu> triangles)
    {
        int bytes = checked(triangles.Length * System.Runtime.InteropServices.Marshal.SizeOf<LumonSceneMeshCardTriangleGpu>());
        if (trianglesCapacityBytes < bytes)
        {
            trianglesSsbo!.EnsureCapacity(bytes, growExponentially: true);
            trianglesCapacityBytes = trianglesSsbo!.SizeBytes;
        }

        trianglesSsbo!.UploadSubData(triangles, dstOffsetBytes: 0, byteCount: bytes);
    }

    private static void UploadPatchMetadata(LumonSceneFieldGpuResources fieldGpu, in LumonSceneMeshCardCaptureJob job)
    {
        uint physicalPageId = job.PhysicalPageId;
        if (physicalPageId == 0u)
        {
            return;
        }

        var meta = new LumonScenePatchMetadataGpu
        {
            OriginWS = new Vector4(job.OriginWS, 0f),
            AxisUWS = new Vector4(job.AxisUWS, 0f),
            AxisVWS = new Vector4(job.AxisVWS, 0f),
            NormalWS = new Vector4(job.NormalWS, 0f),

            // v1: mesh cards occupy exactly one virtual page in the near field (handle mapping is wired later).
            VirtualBasePageX = 0,
            VirtualBasePageY = 0,
            VirtualSizePagesX = 1,
            VirtualSizePagesY = 1,

            ChunkSlot = job.ChunkSlot,
            PatchId = job.PatchId,
            Reserved0 = 0,
            Reserved1 = 0,
        };

        fieldGpu.PatchMetadata.EnsureCreated();
        int stride = System.Runtime.InteropServices.Marshal.SizeOf<LumonScenePatchMetadataGpu>();
        int offsetBytes = checked((int)physicalPageId * stride);
        Span<LumonScenePatchMetadataGpu> one = stackalloc LumonScenePatchMetadataGpu[1];
        one[0] = meta;
        ReadOnlySpan<LumonScenePatchMetadataGpu> ro = one;
        fieldGpu.PatchMetadata.Ssbo.UploadSubData(ro, dstOffsetBytes: offsetBytes);
    }
}
