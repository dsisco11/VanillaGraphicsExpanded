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
    internal static readonly VertexLayoutDesc Layout = new([
        new(0,3,VertexAttribPointerType.Float,VertexInterpretation.Floating,0,0,12),
        new(1,4,VertexAttribPointerType.UnsignedByte,VertexInterpretation.Normalized,1,0,4),
        new(2,1,VertexAttribPointerType.UnsignedInt,VertexInterpretation.Integer,2,0,4),
        new(3,2,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,0,24,1),
        new(4,4,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,4,24,1),
        new(5,1,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,12,24,1),
        new(6,1,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,14,24,1),
        new(7,1,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,16,24,1),
        new(8,1,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,18,24,1),
        new(9,1,VertexAttribPointerType.Short,VertexInterpretation.Normalized,3,20,24,1)]);
    private static readonly ConditionalWeakTable<MeshRef, StorageReceipt> receipts = new();
    private readonly VAO mesh;
    private readonly StorageReceipt receipt;
    private readonly int instances;
    private bool disposed;

    /// <summary>Caches only validated native storage names/capacities; neither mesh nor weather input is retained here.</summary>
    private sealed record StorageReceipt(int Vao, int Elements, int Positions, int Colors, int Flags, int Instances, int Capacity,
        (nint Handle, long Generation) Context);

    #region Public API
    /// <summary>Verifies a new mesh's native layout once, retaining no borrow after this callback-scoped adapter is disposed.</summary>
    internal CloudSimpleGeometry(MeshRef source, int instances)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        if (instances <= 0) throw new ArgumentOutOfRangeException(nameof(instances));
        mesh = source as VAO ?? throw new NotSupportedException("Simple clouds require the native ordinary VAO layout.");
        this.instances = instances;
        if (!receipts.TryGetValue(source, out var cached) || !Matches(mesh, cached) || cached.Capacity < instances)
        {
            cached = Inspect(mesh, instances); receipts.Remove(source); receipts.Add(source, cached);
        }
        receipt = cached;
    }
    /// <inheritdoc />
    internal override void Validate(GraphicsPipelineDesc pipeline, GraphicsDraw draw)
    {
        if (disposed || !Matches(mesh, receipt) || mesh.drawMode != PrimitiveType.Triangles || mesh.IndicesCount != 36
            || pipeline.VertexLayout != Layout || pipeline.Assembly.Topology != PrimitiveType.Triangles
            || pipeline.Assembly.Restart || pipeline.Assembly.FixedIndexRestart || draw != new GraphicsDraw(0,36,0,instances))
            throw new InvalidOperationException("Cloud mesh changed or draw exceeds its verified native instance range.");
    }
    /// <inheritdoc />
    internal override void Submit(GraphicsDraw draw)
    {
        using var vertex = StateCache.Current.BindVertexArrayScope(receipt.Vao);
        using var element = StateCache.Current.BindBufferScope(BufferTarget.ElementArrayBuffer, receipt.Elements);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, IntPtr.Zero, instances);
#if DEBUG
        StateCache.Current.RecordDrawSubmission();
#endif
    }
    /// <summary>Expires only the synchronous adapter; the native mesh retains disposal authority.</summary>
    public override void Dispose() => disposed = true;
    #endregion

    #region Private
    /// <summary>Detects retirement or native storage replacement without driver queries on ordinary draws.</summary>
    private static bool Matches(VAO mesh, StorageReceipt receipt) => !mesh.Disposed && mesh.Initialized
        && receipt.Context == Rendering.Integration.RenderContextRegistry.Current()
        && mesh.VaoId == receipt.Vao && mesh.vboIdIndex == receipt.Elements && mesh.xyzVboId == receipt.Positions
        && mesh.rgbaVboId == receipt.Colors && mesh.flagsVboId == receipt.Flags && mesh.customDataShortVboId == receipt.Instances;

    /// <summary>Certifies the installed 24-byte short instance layout and fixed 24-vertex/36-index template.</summary>
    private static StorageReceipt Inspect(VAO mesh, int instances)
    {
        int[] buffers = [mesh.xyzVboId, mesh.rgbaVboId, mesh.flagsVboId, mesh.customDataShortVboId];
        if (mesh.Disposed || !mesh.Initialized || mesh.VaoId == 0 || mesh.vboIdIndex == 0 || mesh.IndicesCount != 36
            || mesh.drawMode != PrimitiveType.Triangles || instances <= 0 || buffers.Any(id => id == 0))
            throw new InvalidOperationException("Incomplete native Simple cloud storage.");
        using var vertex = StateCache.Current.BindVertexArrayScope(mesh.VaoId);
        using var array = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, 0);
        foreach (var attribute in Layout.Attributes)
        {
            int location = attribute.Location;
            int stride = Read(location, VertexAttribParameter.ArrayStride);
            if (stride == 0) stride = attribute.Components * (attribute.Storage == VertexAttribPointerType.Short ? 2
                : attribute.Storage == VertexAttribPointerType.UnsignedByte ? 1 : 4);
            IntPtr pointer = IntPtr.Zero;
            GL.GetVertexAttribPointer(location, VertexAttribPointerParameter.ArrayPointer, ref pointer);
            if (Read(location, VertexAttribParameter.ArrayEnabled) != 1 || Read(location, VertexAttribParameter.ArraySize) != attribute.Components
                || Read(location, VertexAttribParameter.ArrayType) != (int)attribute.Storage || stride != attribute.Stride
                || Read(location, VertexAttribParameter.ArrayNormalized) != (attribute.Interpretation == VertexInterpretation.Normalized ? 1 : 0)
                || Read(location, VertexAttribParameter.VertexAttribArrayInteger) != (attribute.Interpretation == VertexInterpretation.Integer ? 1 : 0)
                || Read(location, VertexAttribParameter.VertexAttribArrayDivisor) != attribute.Divisor
                || Read(location, (VertexAttribParameter)All.VertexAttribArrayBufferBinding) != buffers[attribute.Binding]
                || pointer != (IntPtr)attribute.Offset)
                throw new NotSupportedException("Unsupported native Simple cloud attribute representation.");
        }
        for (int i = 0; i < buffers.Length; i++)
        {
            StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, buffers[i]);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int bytes);
            int required = i == 3 ? checked(instances * 24) : 24 * Layout.Attributes.First(a => a.Binding == i).Stride;
            if (bytes < required) throw new InvalidOperationException("Cloud stream is smaller than its declared geometry.");
        }
        using var element = StateCache.Current.BindBufferScope(BufferTarget.ElementArrayBuffer, mesh.vboIdIndex);
        GL.GetBufferParameter(BufferTarget.ElementArrayBuffer, BufferParameterName.BufferSize, out int indices);
        if (indices < 36 * sizeof(uint)) throw new InvalidOperationException("Incomplete cloud template indices.");
        return new(mesh.VaoId,mesh.vboIdIndex,buffers[0],buffers[1],buffers[2],buffers[3],instances,Rendering.Integration.RenderContextRegistry.Current());
    }
    /// <summary>Reads one native attribute property during storage certification.</summary>
    private static int Read(int location, VertexAttribParameter parameter) { GL.GetVertexAttrib(location, parameter, out int value); return value; }
    #endregion
}
