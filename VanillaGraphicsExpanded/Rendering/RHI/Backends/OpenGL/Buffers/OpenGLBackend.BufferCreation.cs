using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates existing buffer owners through the private manager transaction.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuBufferObject> CreateBuffer(in RHIBufferDescription description, RHIBufferInitialData initialData)
    {
        // Freeze value intent before native execution or callbacks can observe the caller again.
        var request = description;
        RequireContext();
        var initialization = request.ToInitialization();
        var usage = request.Mode == ResourceStorageMode.Mutable ? request.Usage : BufferUsage.StaticDraw;
        GpuBufferObject owner = request.Role switch
        {
            BufferRole.Index => new GpuEbo(usage, request.Label),
            BufferRole.Uniform => new GpuUniformBuffer(usage, request.Label),
            BufferRole.ShaderStorage => new GpuShaderStorageBuffer(usage, request.Label),
            BufferRole.AtomicCounter => new GpuAtomicCounterBuffer(usage, request.Label),
            BufferRole.IndirectDraw or BufferRole.IndirectDispatch => new GpuIndirectBuffer(usage, request.Label, request.Role),
            BufferRole.PixelPack => new GpuPixelPackBuffer(usage, request.Label),
            BufferRole.PixelUnpack => new GpuPixelUnpackBuffer(usage, request.Label),
            _ => new GpuVbo(request.Role, usage, request.Label)
        };
        try
        {
            if (!RHIResourceManager.CreateBuffer(initialization, owner, initialData, out _).HasValue)
            {
                owner.Dispose();
                return new(null, "Native buffer initialization did not complete.");
            }
            owner.SetDebugName(request.Label);
            return new(owner, null);
        }
        catch
        {
            // The manager has already cleared provisional identity and retired only this candidate.
            // Local declaration cleanup must never replace the initialization exception.
            try { owner.Dispose(); } catch { }
            throw;
        }
    }
    #endregion

}
