using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Minimal RAII wrapper for uploading and binding the shared "object" params UBO
/// (VGE_UBO_OBJECT_BINDING / <see cref="GpuBindingRegistry.Ubo.Object"/>).
/// </summary>
internal sealed class ObjectParamsUbo : IDisposable
{
    private readonly GpuUniformBuffer ubo;

    public ObjectParamsUbo(string debugName)
    {
        ubo = GpuUniformBuffer.Create(debugName: debugName);
    }

    public void UploadAndBind(ReadOnlySpan<byte> bytes)
    {
        ubo.UploadOrResize(bytes, growExponentially: false);
        ubo.BindBase(GpuBindingRegistry.Ubo.Object);
    }

    public void Dispose()
    {
        ubo.Dispose();
    }
}
