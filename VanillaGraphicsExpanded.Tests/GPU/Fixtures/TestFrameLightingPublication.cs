using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns an explicit alternate-view physical storage publication for isolated shader arithmetic.</summary>
internal sealed class TestFrameLightingPublication : IDisposable
{
    private GpuShaderStorageBuffer? buffer;
    #region Public API
    /// <summary>Creates a unit storage scale so existing physical arithmetic observations retain their meaning.</summary>
    internal GpuShaderStorageBuffer Buffer
    {
        get
        {
            if (buffer is null)
            {
                buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.FrameLightingPublication" }).RequireResource();
                Capture();
            }
            return buffer;
        }
    }

    /// <summary>Publishes a complete matching frame/calibration slice without invoking scene owner policy.</summary>
    internal void Capture(float scale = 1, uint frame = 0, uint calibration = 1)
    {
        byte[] bytes = new byte[32];
        Span<float> values = MemoryMarshal.Cast<byte, float>(bytes.AsSpan());
        values[0] = scale; values[1] = 1 / scale; values[2] = 16000 / scale; values[3] = 1;
        Span<uint> identity = MemoryMarshal.Cast<byte, uint>(bytes.AsSpan()); identity[4] = frame; identity[5] = calibration;
        (buffer ??= RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.FrameLightingPublication" }).RequireResource()).UploadData(bytes);
    }

    /// <summary>Assigns only the migrated production program's explicit alternate-view property, retaining caller overrides.</summary>
    internal void Attach(object program)
    {
        var property = program.GetType().GetProperty("FrameLightingInputs", BindingFlags.Instance | BindingFlags.NonPublic);
        if (property is not null && property.GetValue(program) is null) property.SetValue(program, Buffer);
    }

    /// <summary>Supplies physical storage and the immutable material-energy publication to raw native/diagnostic executables.</summary>
    internal void Bind()
    {
        GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, GpuBindingRegistry.Ssbo.FrameLighting, Buffer.BufferId);
        Helpers.MicrofacetTextureReference.Bind();
    }

    /// <summary>Retires the publication after its borrowed programs and draws finish.</summary>
    public void Dispose() { buffer?.Dispose(); buffer = null; }
    #endregion
}
