using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Diagnostics;

/// <summary>Names engine mesh objects at allocation boundaries using their public buffer handles.</summary>
internal static class EngineMeshDebugLabels
{
    #region Public API
    /// <summary>Labels all allocated streams, including custom streams used by pooled and SSBO meshes.</summary>
    [Conditional("DEBUG")]
    internal static void Apply(MeshRef mesh, string? name = null)
    {
        if (mesh is not VAO vao || mesh.Disposed) return;
        string prefix = name ?? $"VS.Mesh{vao.VaoId}";
        GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, vao.VaoId, $"{prefix}.VAO");
        // Zero handles are absent streams; TryLabel safely ignores them without allocating or binding.
        // The SSBO path shares one index buffer across meshes; do not name it after the last pool.
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.vboIdIndex,
            vao.vboIdIndex == ClientPlatformAbstract.singleIndexBufferId ? "VS.Mesh.SharedIndices" : $"{prefix}.Indices");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.xyzVboId, $"{prefix}.Positions");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.normalsVboId, $"{prefix}.Normals");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.uvVboId, $"{prefix}.UV");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.rgbaVboId, $"{prefix}.Colors");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.flagsVboId, $"{prefix}.Flags");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.customDataFloatVboId, $"{prefix}.CustomFloat");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.customDataIntVboId, $"{prefix}.CustomInt");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.customDataShortVboId, $"{prefix}.CustomShort");
        GlDebug.TryLabel(ObjectLabelIdentifier.Buffer, vao.customDataByteVboId, $"{prefix}.CustomByte");
    }
    #endregion
}
