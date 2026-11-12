using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Borrows the installed shape tessellator's indexed animated mesh without acquiring native storage ownership.</summary>
internal sealed class NativeShadowEntityGeometry : IDisposable
{
    private readonly VAO mesh;
    private readonly EngineGeometryInputs native;
    private readonly RHIGeometryBorrow lifetime = new();
    private readonly int position, uv, flags, joints, indices;
    private bool disposed, withdrawn;

    #region Public API
    /// <summary>Retains the exact CPU-known native indexed extent used for command generation.</summary>
    internal int IndexCount { get; }
    /// <summary>Bounds vertex work using the existing position allocation's complete capacity.</summary>
    internal int VertexCapacity { get; }
    /// <summary>Rejects mesh replacement, native deletion, reconfiguration and changed draw extents before submission.</summary>
    internal bool IsCurrent => !disposed && !withdrawn && native.IsPrepared && !mesh.Disposed && mesh.Initialized &&
        mesh.IndicesCount == IndexCount && mesh.xyzVboId == position && mesh.uvVboId == uv &&
        mesh.flagsVboId == flags && mesh.customDataIntVboId == joints && mesh.vboIdIndex == indices;

    /// <summary>Uses the native position/UV/flag/joint declarations established by ShapeTesselator.WithJointIds.</summary>
    internal NativeShadowEntityGeometry(VAO mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.IndicesCount <= 0 || mesh.IndicesCount % 3 != 0 || mesh.drawMode != OpenTK.Graphics.OpenGL.PrimitiveType.Triangles)
            throw new NotSupportedException("Native animated shadow geometry requires indexed triangles.");
        this.mesh = mesh; IndexCount = mesh.IndicesCount;
        position = mesh.xyzVboId; uv = mesh.uvVboId; flags = mesh.flagsVboId;
        joints = mesh.customDataIntVboId; indices = mesh.vboIdIndex;
        using (var positions = new EngineBufferReference(position))
        {
            if (!positions.Reference.TryGetMetadata(out var metadata) || metadata.Capacity <= 0 || metadata.Capacity % 12 != 0)
                throw new InvalidOperationException("Native animated position storage is incomplete.");
            VertexCapacity = checked((int)(metadata.Capacity / 12));
        }
        native = new(mesh, new RHIVertexLayout([
            new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
            new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4),
            new(5, 1, VertexComponentFormat.SignedInt, VertexInterpretation.Integer, 5, 0, 4)]),
            new Dictionary<int, int> { [0] = position, [1] = uv, [3] = flags, [5] = joints },
            VertexCapacity, IndexCount, invalidated: Withdraw);
    }

    /// <summary>Adds a private selector stream while retaining the native mesh's own vertex and index allocations.</summary>
    internal RHIGeometryInputs WithView(RHIBufferRange selector)
    {
        if (!IsCurrent || selector.Length != 8) throw new InvalidOperationException("Native entity view input is unavailable.");
        var layout = new RHIVertexLayout(native.Inputs.Layout.Attributes.Select(attribute =>
            attribute.Location == 3 ? attribute with { Storage = VertexComponentFormat.SignedInt } : attribute).Append(
            new RHIVertexAttribute(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 10, 0, 8, 1)));
        return new(layout, native.Inputs.Streams.Append(new RHIVertexStream(10, selector, 8, 1)), native.Inputs.Indices, 0, lifetime);
    }

    /// <summary>Releases only private borrow authority; the native renderer continues to own the mesh.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; lifetime.Dispose(); native.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws every derived packet when the canonical native configuration changes.</summary>
    private void Withdraw() { withdrawn = true; lifetime.Dispose(); }
    #endregion
}
