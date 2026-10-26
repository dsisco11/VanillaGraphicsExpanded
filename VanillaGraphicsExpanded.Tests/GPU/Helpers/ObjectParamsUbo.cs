using VanillaGraphicsExpanded.Rendering.RHI;
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
        ubo = RHICommandContext.Current.CreateUniformBuffer(new RHIBufferDescription { Role = RHIBufferRole.Uniform, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = debugName }).RequireResource();
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
