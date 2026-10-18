using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns typed cloud compute execution and bounded diagnostic-buffer readback for cloud tests.</summary>
internal sealed class CloudTraversalProbe : IDisposable
{
    private readonly CloudTraversalComputeShader program;
    private readonly GpuShaderStorageBuffer output;

    #region Public API
    /// <summary>Prepares the registered fixture binary and adopts its result storage exception-safely.</summary>
    internal CloudTraversalProbe(ICoreAPI api)
    {
        program = CloudTraversalComputeShader.Create(api);
        try
        {
            if (!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            output = GpuShaderStorageBuffer.Create(debugName: "Tests.Cloud.Traversal.Results");
        }
        catch { program.Dispose(); throw; }
    }

    /// <summary>Publishes a complete retained input set, dispatches one invocation and reads its diagnostic records.</summary>
    internal Vector4[] Run(CloudVolumeParameters volume, GpuTexture map, Vector3 origin, Vector3 direction,
        float limit, int mode, ReadOnlySpan<float> positions = default, GpuTexture? bounds = null, int? root = null)
    {
        if (positions.Length > 402) throw new ArgumentOutOfRangeException(nameof(positions));
        var records = new Vector4[403];
        for (int i = 0; i < positions.Length; i++) records[i].X = positions[i];
        output.UploadData(records);
        program.SetVolume(volume);
        program.SetRay(origin, direction, mode is 6 or 11 ? positions.Length : limit, mode);
        program.SetTraversal(root);
        program.Map = map;
        program.HeightBounds = bounds;
        program.Result = output;
        program.Dispatch(1);
        // Publication and resource borrowing use the actual owner; only the fixture reads GPU results.
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var mapped = output.MapScope<Vector4>(0, records.Length, MapBufferAccessMask.MapReadBit);
        return mapped.Span.ToArray();
    }

    /// <summary>Retires the shader before its borrowed storage, preserving enclosing program scopes.</summary>
    public void Dispose() => RenderingCleanup.Run(program.Dispose, output.Dispose);
    #endregion
}
