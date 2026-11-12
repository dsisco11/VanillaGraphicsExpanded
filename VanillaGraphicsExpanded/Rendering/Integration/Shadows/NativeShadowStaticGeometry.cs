using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Borrows standard indexed object storage and an explicitly declared native matrix-instance stream.</summary>
internal sealed class NativeShadowStaticGeometry : IDisposable
{
    private readonly VAO mesh;
    private readonly EngineGeometryInputs native;
    private readonly RHIGeometryBorrow lifetime = new();
    private readonly int position, uv, flags, transforms, indices;
    private bool disposed, withdrawn;

    #region Public API
    internal int IndexCount { get; }
    internal int Instances { get; }
    internal bool Instanced { get; }
    /// <summary>Rejects original storage or declaration changes without taking ownership of native mesh lifetime.</summary>
    internal bool IsCurrent => !disposed && !withdrawn && native.IsPrepared && !mesh.Disposed && mesh.Initialized &&
        mesh.IndicesCount == IndexCount && mesh.xyzVboId == position && mesh.uvVboId == uv &&
        mesh.flagsVboId == flags && mesh.vboIdIndex == indices && (!Instanced || mesh.customDataFloatVboId == transforms);

    /// <summary>Requires the owner to declare original instance count and matrix packing instead of guessing from a VAO.</summary>
    internal NativeShadowStaticGeometry(VAO mesh, int vertices, int instances = 1, bool instanced = false)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (vertices <= 0 || instances <= 0 || (!instanced && instances != 1) || mesh.IndicesCount <= 0 ||
            mesh.IndicesCount % 3 != 0 || mesh.drawMode != OpenTK.Graphics.OpenGL.PrimitiveType.Triangles)
            throw new NotSupportedException("Native standard shadow geometry requires complete declared indexed triangles.");
        this.mesh = mesh; IndexCount = mesh.IndicesCount; Instances = instances; Instanced = instanced;
        position = mesh.xyzVboId; uv = mesh.uvVboId; flags = mesh.flagsVboId;
        indices = mesh.vboIdIndex; transforms = mesh.customDataFloatVboId;
        var attributes = new List<RHIVertexAttribute>
        {
            new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
            new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4)
        };
        var streams = new Dictionary<int, int> { [0] = position, [1] = uv, [3] = flags };
        if (instanced)
        {
            for (int column = 0; column < 4; column++)
                attributes.Add(new(4 + column, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 6, column * 16, 64, 1));
            streams[6] = transforms;
        }
        native = new(mesh, new RHIVertexLayout(attributes), streams, vertices, IndexCount, instances, Withdraw);
    }

    /// <summary>Keeps all original object instances while repeating one compact view selector across their draw.</summary>
    internal RHIGeometryInputs WithView(RHIBufferRange selector)
    {
        if (!IsCurrent || selector.Length != 8) throw new InvalidOperationException("Native object selector is unavailable.");
        // Base-instance remains zero. A divisor equal to the complete original
        // instance count selects this one view without shifting any native stream.
        // Only the private shadow realization remaps native custom-float locations
        // 4..7 to the packaged matrix input at 6..9. The original VAO is unchanged.
        var layout = new RHIVertexLayout(native.Inputs.Layout.Attributes.Select(attribute =>
            attribute.Binding == 6 ? attribute with { Location = attribute.Location + 2 } :
            attribute.Location == 3 ? attribute with { Storage = VertexComponentFormat.SignedInt } : attribute).Append(
            new RHIVertexAttribute(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 10, 0, 8, Instances)));
        return new(layout, native.Inputs.Streams.Append(new RHIVertexStream(10, selector, 8, Instances)), native.Inputs.Indices, 0, lifetime);
    }

    /// <summary>Retires only private input realizations and borrowing authority.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Dispose(); native.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws derived geometry when the native configuration expires.</summary>
    private void Withdraw() { withdrawn = true; lifetime.Dispose(); }
    #endregion
}
