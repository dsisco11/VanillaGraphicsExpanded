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
    private readonly EngineBufferReference? faces;
    private readonly int faceBytes, sharedIndex;
    private bool disposed;
    private bool withdrawn;

    // Terrain shadow inputs are not the liquid layout. Displacement resolves the
    // material rectangle through the existing UV atlas lookup rather than liquid data.
    internal static readonly RHIVertexLayout OrdinaryLayout = new([
        new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12),
        new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 1, 0, 8),
        new(2, 4, VertexComponentFormat.UnsignedByte, VertexInterpretation.Normalized, 2, 0, 4),
        new(3, 1, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 3, 0, 4)]);

    #region Public API
    /// <summary>Reports synchronous native storage/configuration withdrawal.</summary>
    internal bool IsPrepared => !disposed && !withdrawn && native.IsPrepared &&
        (faces is null || (faces.IsValid && sharedIndex == ClientPlatformAbstract.singleIndexBufferId));

    /// <summary>Supplies the native face-data allocation as a non-owning exact storage interval.</summary>
    internal GpuStorageBufferBinding Faces => faces is null ? default : new(faces.Reference, 0, faceBytes);

    /// <summary>Admits the installed ordinary pool declaration without inspecting or altering its VAO.</summary>
    internal NativeShadowTerrainGeometry(VAO mesh, int vertices, int indices, bool storage = false)
    {
        if (storage)
        {
            // Native SSBO terrain addresses 64-byte quad records using gl_VertexID/4
            // and the current shared quad index allocation, which can outlive a VAO's saved EBO name.
            if (vertices % 4 != 0 || indices % 6 != 0 || GpuSupport.Graphics.MaxVertexShaderStorageBlocks < 1)
                throw new NotSupportedException("Packed native terrain requires complete quad storage and its native vertex SSBO capability.");
            sharedIndex = ClientPlatformAbstract.singleIndexBufferId;
            faceBytes = checked(vertices * 16);
            faces = new(mesh.xyzVboId);
            try
            {
                if (!faces.Reference.TryGetMetadata(out var metadata) || metadata.Capacity < faceBytes)
                    throw new InvalidOperationException("Native face-data storage is incomplete.");
                native = new(mesh, new RHIVertexLayout([
                    new(0, 4, VertexComponentFormat.UnsignedByte, VertexInterpretation.Normalized, 0, 0, 4)]),
                    new Dictionary<int, int> { [0] = mesh.rgbaVboId }, vertices, indices,
                    invalidated: Withdraw, indexBuffer: sharedIndex);
                faces.ObservePreparation(this, Withdraw);
            }
            catch { native?.Dispose(); faces.Dispose(); lifetime.Dispose(); throw; }
        }
        else
            native = new(mesh, OrdinaryLayout, new Dictionary<int, int>
            {
                [0] = mesh.xyzVboId, [1] = mesh.uvVboId, [2] = mesh.rgbaVboId,
                [3] = mesh.flagsVboId
            }, vertices, indices, invalidated: Withdraw);
    }

    /// <summary>Adds compact object/view indices to a private input; native mesh-pool streams remain unchanged.</summary>
    internal RHIGeometryInputs WithViews(RHIBufferRange viewReferences)
    {
        if (!IsPrepared) throw new InvalidOperationException("Terrain storage was withdrawn.");
        // A distinct packet deliberately omits the imported VAO certificate: the RHI
        // realizes its own VAO, while this borrow expires with the original certificate.
        RHIVertexAttribute[] attributes = [
            new(10, 2, VertexComponentFormat.UnsignedInt, VertexInterpretation.Integer, 10, 0, 8, 1)];
        // The installed VAO declares unsigned flag storage. Our private realization
        // presents the same bits as the material's signed integer input.
        var layout = new RHIVertexLayout(native.Inputs.Layout.Attributes.Select(attribute =>
            attribute.Location == 3 ? attribute with { Storage = VertexComponentFormat.SignedInt } : attribute).Concat(attributes));
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
        faces?.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws every derived input when any borrowed stream changes structurally.</summary>
    private void Withdraw()
    {
        withdrawn = true;
        lifetime.Dispose();
    }
    #endregion
}
