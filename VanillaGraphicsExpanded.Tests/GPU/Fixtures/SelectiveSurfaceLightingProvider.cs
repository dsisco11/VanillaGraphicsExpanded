using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Withholds alternate real cache pages without changing producer resources or dependency identity.</summary>
internal sealed class SelectiveSurfaceLightingProvider(ISurfaceLightingProvider source) : ISurfaceLightingProvider, IDisposable
{
    private readonly GpuShaderStorageBuffer readiness=GpuShaderStorageBuffer.Create();
    public bool Withhold { get; set; }=true;

    #region Provider lifetime
    /// <summary>Copies published readiness and masks selected pages while preserving all real captured lighting.</summary>
    public bool TryGetSurfaceLighting(out SurfaceLightingSnapshot snapshot)
    {
        if(!source.TryGetSurfaceLighting(out snapshot)) return false;
        if(!Withhold) return true;
        int bytes=snapshot.Readiness.SizeBytes;
        using var read=snapshot.Readiness.MapRange<uint>(0,bytes>>2,MapBufferAccessMask.MapReadBit);
        Assert.True(read.IsMapped);
        var flags=read.Span.ToArray();
        for(int i=1;i<flags.Length;i+=2) flags[i]=0;
        readiness.EnsureCapacity(bytes,growExponentially:false);
        readiness.UploadSubData<uint>(flags,0,bytes);
        snapshot=snapshot with { Readiness=readiness };
        return true;
    }

    /// <summary>Releases only the provider's private readiness copy.</summary>
    public void Dispose() => readiness.Dispose();
    #endregion
}
