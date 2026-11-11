using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Borrows ordinary terrain storage into a private compact object/view vertex input.</summary>
internal sealed class NativeShadowTerrainGeometry : IDisposable
{
    private readonly EngineGeometryInputs native;
    private readonly RHIGeometryBorrow lifetime = new();
    private bool disposed;

    // Terrain shadow inputs are not the liquid layout. Displacement resolves the
    // material rectangle through the existing UV atlas lookup rather than liquid data.
    internal static readonly RHIVertexLayout OrdinaryLayout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
        new(2, 4, VertexComponentFormat.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
        new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4)]);

    #region Public API
    /// <summary>Reports synchronous native storage/configuration withdrawal.</summary>
    internal bool IsPrepared => !disposed && native.IsPrepared;

    /// <summary>Admits the installed ordinary pool declaration without inspecting or altering its VAO.</summary>
    internal NativeShadowTerrainGeometry(VAO mesh, int vertices, int indices)
    {
        native = new(mesh, OrdinaryLayout, new Dictionary<int, int>
        {
            [0] = mesh.xyzVboId, [1] = mesh.uvVboId, [2] = mesh.rgbaVboId,
            [3] = mesh.flagsVboId
        }, vertices, indices, invalidated: lifetime.Dispose);
    }

    /// <summary>Adds compact object/view indices to a private input; native mesh-pool streams remain unchanged.</summary>
    internal RHIGeometryInputs WithViews(RHIBufferRange viewReferences)
    {
        if (!IsPrepared) throw new InvalidOperationException("Terrain storage was withdrawn.");
        // A distinct packet deliberately omits the imported VAO certificate: the RHI
        // realizes its own VAO, while this borrow expires with the original certificate.
        RHIVertexAttribute[] attributes = [
            new(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 10, 0, 8, 1)];
        var layout = new RHIVertexLayout(native.Inputs.Layout.Attributes.Concat(attributes));
        var streams = native.Inputs.Streams.Append(new RHIVertexStream(10, viewReferences, 8, 1));
        return new(layout, streams, native.Inputs.Indices, 0, lifetime);
    }

    /// <summary>Retires only adapter realizations, leaving native buffers and VAOs owned by the game.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Dispose();
        native.Dispose();
    }
    #endregion
}
