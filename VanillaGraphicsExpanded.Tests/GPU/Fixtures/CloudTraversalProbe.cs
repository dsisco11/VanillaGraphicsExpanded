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
    internal CloudTraversalProbe(ICoreAPI api,bool selfShadowing=true)
    {
        program = CloudTraversalComputeShader.Create(api,selfShadowing);
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

    /// <summary>Times the authored lookup dispatch, including activation, with result readback outside the GPU interval.</summary>
    internal double MeasureShadowLookup(CloudVolumeParameters volume,GpuTexture map,bool filtered,GpuTimerQuery timer)
    {
        output.UploadData(new Vector4[403]);
        program.SetVolume(volume);
        program.SetRay(Vector3.Zero,new(40,.5f,0),60,filtered?16:17);
        program.SetTraversal(null);
        program.Map=map;program.HeightBounds=null;program.Result=output;
        timer.Begin();program.Dispatch(20,20);timer.End();
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        return timer.GetResultNanoseconds()/1e6;
    }

    /// <summary>Retires the shader before its borrowed storage, preserving enclosing program scopes.</summary>
    public void Dispose() => RenderingCleanup.Run(program.Dispose, output.Dispose);
    #endregion
}
