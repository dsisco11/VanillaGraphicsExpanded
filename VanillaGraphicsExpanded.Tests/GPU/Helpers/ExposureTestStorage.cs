using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;
/// <summary>Allocates exposure inputs and synchronously inspects completed buffer storage.</summary>
internal static class ExposureTestStorage
{
    #region Public API
    /// <summary>Creates one initialized exposure EV value.</summary>
    internal static GpuShaderStorageBuffer Create(float value)
    {
        var buffer = GpuShaderStorageBuffer.Create(debugName: "Tests.Exposure");
        buffer.UploadData(new[] { value });
        return buffer;
    }
    /// <summary>Reads completed GPU storage while preserving native buffer bindings.</summary>
    internal static T[] Read<T>(GpuShaderStorageBuffer buffer) where T : unmanaged
    {
        GL.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit);
        var values = new T[buffer.SizeBytes / System.Runtime.InteropServices.Marshal.SizeOf<T>()];
        using (buffer.BindScope()) GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, buffer.SizeBytes, values);
        return values;
    }
    #endregion
}
