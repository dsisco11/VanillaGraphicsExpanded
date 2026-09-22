using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Checks binding points against the active graphics context.</summary>
internal static partial class GpuBindingRegistry
{
    /// <summary>
    /// Throws if a binding index is outside the current context limit.
    /// This is a helper for development-time assertions.
    /// </summary>
    public static void ThrowIfOutOfRangeUbo(int bindingPoint)
    {
        if (bindingPoint < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingPoint));
        }

        if (GpuSupport.MaxUniformBufferBindings > 0 && bindingPoint >= GpuSupport.MaxUniformBufferBindings)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingPoint), $"UBO binding point {bindingPoint} exceeds GL_MAX_UNIFORM_BUFFER_BINDINGS={GpuSupport.MaxUniformBufferBindings}.");
        }
    }
}

