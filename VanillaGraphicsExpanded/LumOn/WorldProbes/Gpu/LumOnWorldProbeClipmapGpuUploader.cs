using System;
using System.Collections.Generic;
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

    private readonly GpuVao vao;
    private readonly GpuVbo vbo;

    private bool isDisposed;

    public LumOnWorldProbeClipmapGpuUploader(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));

        vao = GpuVao.Create(debugName: "VGE_WorldProbeClipmapUpload_VAO");
        vbo = GpuVbo.Create(BufferTarget.ArrayBuffer, BufferUsageHint.StreamDraw, debugName: "VGE_WorldProbeClipmapUpload_VBO");

        using var vaoScope = vao.BindScope();
        using var vboScope = vbo.BindScope();

        int stride = Marshal.SizeOf<UploadVertex>();

        // vec2 atlasCoord
        vao.EnableAttrib(0);
        vao.AttribPointer(0, 2, VertexAttribPointerType.Float, normalized: false, stride, 0);

        // vec4 shR
        vao.EnableAttrib(1);
        vao.AttribPointer(1, 4, VertexAttribPointerType.Float, normalized: false, stride, 8);

        // vec4 shG
        vao.EnableAttrib(2);
        vao.AttribPointer(2, 4, VertexAttribPointerType.Float, normalized: false, stride, 24);

        // vec4 shB
        vao.EnableAttrib(3);
        vao.AttribPointer(3, 4, VertexAttribPointerType.Float, normalized: false, stride, 40);

        // vec3 aoDir
        vao.EnableAttrib(4);
        vao.AttribPointer(4, 3, VertexAttribPointerType.Float, normalized: false, stride, 56);

        // float aoConfidence
        vao.EnableAttrib(5);
        vao.AttribPointer(5, 1, VertexAttribPointerType.Float, normalized: false, stride, 68);

        // float confidence
        vao.EnableAttrib(6);
        vao.AttribPointer(6, 1, VertexAttribPointerType.Float, normalized: false, stride, 72);

        // float meanLogHitDistance
        vao.EnableAttrib(7);
        vao.AttribPointer(7, 1, VertexAttribPointerType.Float, normalized: false, stride, 76);

        // vec4 shSky
        vao.EnableAttrib(8);
        vao.AttribPointer(8, 4, VertexAttribPointerType.Float, normalized: false, stride, 80);

        // float skyIntensity
        vao.EnableAttrib(9);
        vao.AttribPointer(9, 1, VertexAttribPointerType.Float, normalized: false, stride, 96);

        // uint flags (integer attribute)
        vao.EnableAttrib(10);
        vao.AttribIPointer(10, 1, VertexAttribIntegerType.UnsignedInt, stride, 100);
    }

    public int Upload(
        LumOnWorldProbeClipmapGpuResources resources,
        IReadOnlyList<LumOnWorldProbeTraceResult> results,
        int uploadBudgetBytesPerFrame)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (results is null) throw new ArgumentNullException(nameof(results));

        var prog = capi.Shader.GetProgramByName("lumon_worldprobe_clipmap_resolve") as LumOnWorldProbeClipmapResolveShaderProgram;
        if (prog is null || prog.LoadError || prog.Disposed)
        {
            return 0;
        }

        int maxVertices = results.Count;
        int bytesPerVertex = Marshal.SizeOf<UploadVertex>();
        if (uploadBudgetBytesPerFrame > 0)
        {
            maxVertices = Math.Min(maxVertices, Math.Max(0, uploadBudgetBytesPerFrame / Math.Max(1, bytesPerVertex)));
        }

        int used = 0;
        var vertices = new List<UploadVertex>(capacity: maxVertices);
        for (int i = 0; i < results.Count && used < maxVertices; i++)
        {
            var r = results[i];
            int level = r.Request.Level;
            if ((uint)level >= (uint)resources.Levels) continue;

            Vec3i s = r.Request.StorageIndex;

            int u = s.X + s.Z * resources.Resolution;
            int v = s.Y + level * resources.AtlasHeightPerLevel;

            uint flags = LumOnWorldProbeMetaFlags.Valid;
            if (r.MeanLogHitDistance <= 0f && r.ShortRangeAoConfidence > 0.99f)
            {
                flags |= LumOnWorldProbeMetaFlags.SkyOnly;
            }

            vertices.Add(UploadVertex.From(r, u, v, flags));
            used++;
        }

        if (vertices.Count == 0)
        {
            return 0;
        }

        using var gpuScope = GlGpuProfiler.Instance.Scope("LumOn.WorldProbe.UploadResolve");

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        prog.Use();
        prog.AtlasSize = new Vec2f(resources.AtlasWidth, resources.AtlasHeight);

        using var vaoScope = vao.BindScope();

        var fbo = resources.GetFbo();
        fbo.Bind();
        GL.Viewport(0, 0, resources.AtlasWidth, resources.AtlasHeight);

        UploadVertex[] data = vertices.ToArray();

        vbo.UploadData(data);

        GL.DrawArrays(PrimitiveType.Points, 0, data.Length);
        Rendering.GpuFramebuffer.Unbind();
        prog.Stop();

        return data.Length;
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        vbo.Dispose();
        vao.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct UploadVertex
    {
        public float AtlasU;
        public float AtlasV;

        public float ShR0;
        public float ShR1;
        public float ShR2;
        public float ShR3;

        public float ShG0;
        public float ShG1;
        public float ShG2;
        public float ShG3;

        public float ShB0;
        public float ShB1;
        public float ShB2;
        public float ShB3;

        public float AoDirX;
        public float AoDirY;
        public float AoDirZ;

        public float AoConfidence;
        public float Confidence;
        public float MeanLogHitDistance;

        public float ShSky0;
        public float ShSky1;
        public float ShSky2;
        public float ShSky3;

        public float SkyIntensity;

        public uint Flags;

        public static UploadVertex From(in LumOnWorldProbeTraceResult r, int atlasU, int atlasV, uint flags)
        {
            return new UploadVertex
            {
                AtlasU = atlasU,
                AtlasV = atlasV,

                ShR0 = r.ShR.X,
                ShR1 = r.ShR.Y,
                ShR2 = r.ShR.Z,
                ShR3 = r.ShR.W,

                ShG0 = r.ShG.X,
                ShG1 = r.ShG.Y,
                ShG2 = r.ShG.Z,
                ShG3 = r.ShG.W,

                ShB0 = r.ShB.X,
                ShB1 = r.ShB.Y,
                ShB2 = r.ShB.Z,
                ShB3 = r.ShB.W,

                AoDirX = r.ShortRangeAoDirWorld.X,
                AoDirY = r.ShortRangeAoDirWorld.Y,
                AoDirZ = r.ShortRangeAoDirWorld.Z,

                AoConfidence = r.ShortRangeAoConfidence,
                Confidence = r.Confidence,
                MeanLogHitDistance = r.MeanLogHitDistance,

                ShSky0 = r.ShSky.X,
                ShSky1 = r.ShSky.Y,
                ShSky2 = r.ShSky.Z,
                ShSky3 = r.ShSky.W,

                SkyIntensity = r.SkyIntensity,

                Flags = flags,
            };
        }
    }
}
