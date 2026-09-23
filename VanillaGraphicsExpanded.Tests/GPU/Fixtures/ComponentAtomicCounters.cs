using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Authors and observes counters while production shaders own their binding points.</summary>
internal sealed class ComponentAtomicCounters : IDisposable
{
    public GpuAtomicCounterBuffer Buffer { get; } = GpuAtomicCounterBuffer.Create();
    #region Population and observation
    /// <summary>Allocates zeroed counters and initializes the first value used by the scenario.</summary>
    public ComponentAtomicCounters(uint initialValue = 0, int counterCount = 1)
    {
        Buffer.InitializeCounters(counterCount);
        Upload(initialValue);
    }
    /// <summary>Updates the first counter without replacing its allocation.</summary>
    public void Upload(uint value) => Buffer.UploadSubData<uint>([value], 0, sizeof(uint));
    /// <summary>Resets all requested counters for another dispatch.</summary>
    public void UploadZeros(int counterCount) => Buffer.UploadSubData(new uint[counterCount], 0);
    /// <summary>Observes the first result after the caller's completion fence.</summary>
    public uint Read() => Read(1)[0];
    /// <summary>Copies the requested counters through production buffer mapping.</summary>
    public uint[] Read(int counterCount)
    {
        using var mapped = Buffer.MapRange<uint>(0, counterCount, MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }
    /// <summary>Releases the owned counter storage.</summary>
    public void Dispose() => Buffer.Dispose();
    #endregion
}
