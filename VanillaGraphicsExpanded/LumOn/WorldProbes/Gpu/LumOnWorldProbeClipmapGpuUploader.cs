using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

internal sealed class LumOnWorldProbeClipmapGpuUploader : IDisposable
{
    private readonly ICoreClientAPI capi;

    private readonly int vao;
    private readonly int vbo;

    private bool isDisposed;

    public LumOnWorldProbeClipmapGpuUploader(ICoreClientAPI capi)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));

        vao = GL.GenVertexArray();
        vbo = GL.GenBuffer();

        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

        int stride = Marshal.SizeOf<UploadVertex>();

        // vec2 atlasCoord
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, normalized: false, stride, 0);

        // vec4 shR
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 8);

        // vec4 shG
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 24);

        // vec4 shB
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, 40);

        // vec3 aoDir
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, stride, 56);

        // float aoConfidence
        GL.EnableVertexAttribArray(5);
        GL.VertexAttribPointer(5, 1, VertexAttribPointerType.Float, false, stride, 68);

        // float confidence
        GL.EnableVertexAttribArray(6);
        GL.VertexAttribPointer(6, 1, VertexAttribPointerType.Float, false, stride, 72);

        // float meanLogHitDistance
        GL.EnableVertexAttribArray(7);
        GL.VertexAttribPointer(7, 1, VertexAttribPointerType.Float, false, stride, 76);

        // uint flags (integer attribute)
        GL.EnableVertexAttribArray(8);
        GL.VertexAttribIPointer(8, 1, VertexAttribIntegerType.UnsignedInt, stride, (IntPtr)80);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);
    }

    public void Upload(
        LumOnWorldProbeClipmapGpuResources resources,
        IReadOnlyList<LumOnWorldProbeTraceResult> results,
        int uploadBudgetBytesPerFrame)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));
        if (results is null) throw new ArgumentNullException(nameof(results));

        var prog = capi.Shader.GetProgramByName("lumon_worldprobe_clipmap_resolve") as LumOnWorldProbeClipmapResolveShaderProgram;
        if (prog is null || prog.LoadError || prog.Disposed)
        {
            return;
        }

        int maxVertices = results.Count;
        int bytesPerVertex = Marshal.SizeOf<UploadVertex>();
        if (uploadBudgetBytesPerFrame > 0)
        {
            maxVertices = Math.Min(maxVertices, Math.Max(0, uploadBudgetBytesPerFrame / Math.Max(1, bytesPerVertex)));
        }

        // Group by level (stable order).
        var perLevel = new List<UploadVertex>[resources.Levels];
        for (int i = 0; i < perLevel.Length; i++) perLevel[i] = new List<UploadVertex>();

        int used = 0;
        for (int i = 0; i < results.Count && used < maxVertices; i++)
        {
            var r = results[i];
            int level = r.Request.Level;
            if ((uint)level >= (uint)resources.Levels) continue;

            Vec3i s = r.Request.StorageIndex;

            int u = s.X + s.Z * resources.Resolution;
            int v = s.Y;

            uint flags = LumOnWorldProbeMetaFlags.Valid;
            if (r.MeanLogHitDistance <= 0f && r.ShortRangeAoConfidence > 0.99f)
            {
                flags |= LumOnWorldProbeMetaFlags.SkyOnly;
            }

            perLevel[level].Add(UploadVertex.From(r, u, v, flags));
            used++;
        }

        GL.Disable(EnableCap.Blend);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        prog.Use();
        prog.AtlasSize = new Vec2f(resources.AtlasWidth, resources.AtlasHeight);

        GL.BindVertexArray(vao);

        for (int level = 0; level < resources.Levels; level++)
        {
            var list = perLevel[level];
            if (list.Count == 0) continue;

            var fbo = resources.GetFbo(level);
            fbo.Bind();
            GL.Viewport(0, 0, resources.AtlasWidth, resources.AtlasHeight);

            UploadVertex[] data = list.ToArray();

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * bytesPerVertex, data, BufferUsageHint.StreamDraw);

            GL.DrawArrays(PrimitiveType.Points, 0, data.Length);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            Rendering.GBuffer.Unbind();
        }

        GL.BindVertexArray(0);
        prog.Stop();
    }

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        GL.DeleteBuffer(vbo);
        GL.DeleteVertexArray(vao);
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

                Flags = flags,
            };
        }
    }
}
