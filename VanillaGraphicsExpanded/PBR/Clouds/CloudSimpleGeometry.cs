using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Borrows an ordinary native cloud VAO for one synchronous indexed instance draw.</summary>
internal sealed class CloudSimpleGeometry : GraphicsGeometry
{
    internal static readonly RHIVertexLayout Layout = new([
        new(0,3,VertexComponentFormat.Float,VertexInterpretation.Floating,0,0,12),
        new(1,4,VertexComponentFormat.UnsignedByte,VertexInterpretation.Normalized,1,0,4),
        new(2,1,VertexComponentFormat.UnsignedInt,VertexInterpretation.Integer,2,0,4),
        new(3,2,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,0,24,1),
        new(4,4,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,4,24,1),
        new(5,1,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,12,24,1),
        new(6,1,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,14,24,1),
        new(7,1,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,16,24,1),
        new(8,1,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,18,24,1),
        new(9,1,VertexComponentFormat.SignedShort,VertexInterpretation.Normalized,3,20,24,1)]);
    private static readonly ConditionalWeakTable<MeshRef, StorageReceipt> receipts = new();
    private readonly VAO mesh;
    private readonly StorageReceipt receipt;
    private readonly RHIGeometryBorrow borrow = new();
    private readonly RHIGeometryInputs inputs;

    /// <summary>Caches only validated native storage names/capacities; neither mesh nor weather input is retained here.</summary>
    private sealed record StorageReceipt(int Vao, int Elements, int Positions, int Colors, int Flags, int Instances, int Capacity,
        EngineGeometryInputs Geometry);

    #region Public API
    /// <summary>Verifies a new mesh's native layout once, retaining no borrow after this callback-scoped adapter is disposed.</summary>
    internal CloudSimpleGeometry(MeshRef source, int instances)
    {
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
        if (instances <= 0) throw new ArgumentOutOfRangeException(nameof(instances));
        mesh = source as VAO ?? throw new NotSupportedException("Simple clouds require the native ordinary VAO layout.");
        if (!receipts.TryGetValue(source, out var cached) || !Matches(mesh, cached) || cached.Capacity < instances)
        {
            var replacement = Inspect(mesh, instances);
            receipts.Remove(source); cached?.Geometry.Dispose();
            cached = replacement; receipts.Add(source, cached);
        }
        receipt = cached;
        inputs = new(receipt.Geometry.Inputs, borrow);
    }
    /// <summary>Lends the certified packet only for this synchronous engine callback.</summary>
    internal override RHIGeometryInputs Inputs
    {
        get
        {
            borrow.RequireLive();
            if (!Matches(mesh, receipt) || mesh.drawMode != PrimitiveType.Triangles || mesh.IndicesCount != 36)
                throw new InvalidOperationException("Cloud storage changed after its synchronous borrow began.");
            return inputs;
        }
    }
    /// <summary>Expires only the synchronous adapter; the native mesh retains disposal authority.</summary>
    public override void Dispose() => borrow.Dispose();
    #endregion

    #region Private
    /// <summary>Detects retirement or native storage replacement without driver queries on ordinary draws.</summary>
    private static bool Matches(VAO mesh, StorageReceipt receipt) => !mesh.Disposed && mesh.Initialized
        && GpuResourceManager.IsEngineGeometryConfigurationCurrent(receipt.Geometry.Inputs.ImportedArray!)
        && receipt.Geometry.Inputs.Streams.All(stream => stream.Range.Buffer.TryGetMetadata(out var data) && data.Capacity >= stream.Range.Offset + stream.Range.Length)
        && mesh.VaoId == receipt.Vao && mesh.vboIdIndex == receipt.Elements && mesh.xyzVboId == receipt.Positions
        && mesh.rgbaVboId == receipt.Colors && mesh.flagsVboId == receipt.Flags && mesh.customDataShortVboId == receipt.Instances;

    /// <summary>Certifies the installed 24-byte short instance layout and fixed 24-vertex/36-index template.</summary>
    private static StorageReceipt Inspect(VAO mesh, int instances)
    {
        int[] buffers = [mesh.xyzVboId, mesh.rgbaVboId, mesh.flagsVboId, mesh.customDataShortVboId];
        if (mesh.Disposed || !mesh.Initialized || mesh.VaoId == 0 || mesh.vboIdIndex == 0 || mesh.IndicesCount != 36
            || mesh.drawMode != PrimitiveType.Triangles || instances <= 0 || buffers.Any(id => id == 0))
            throw new InvalidOperationException("Incomplete native Simple cloud storage.");
        var geometry = new EngineGeometryInputs(mesh, Layout,
            new Dictionary<int, int> { [0] = buffers[0], [1] = buffers[1], [2] = buffers[2], [3] = buffers[3] }, 24, 36, instances);
        return new(mesh.VaoId, mesh.vboIdIndex, buffers[0], buffers[1], buffers[2], buffers[3], instances,
            geometry);
    }
    #endregion
}
