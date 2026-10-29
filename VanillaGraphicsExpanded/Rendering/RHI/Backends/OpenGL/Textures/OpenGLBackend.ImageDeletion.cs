using System;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Deletes original image identities only after existing retirement admission.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Deletes an admitted texture name and reconciles its aliases and bindings.</summary>
    internal void DeleteTexture(int texture)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteTexture(texture);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Texture, texture);
    }
    /// <summary>Deletes an admitted renderbuffer name without changing unrelated framebuffer selection.</summary>
    internal void DeleteRenderbuffer(int renderbuffer)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteRenderbuffer(renderbuffer);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Renderbuffer, renderbuffer);
    }
    /// <summary>Deletes an admitted sampler and records native implicit unit unbinding.</summary>
    internal void DeleteSampler(int sampler)
    {
        RequireContext();
        OpenGLDriverCalls.DeleteSampler(sampler);
        RequireContext();
        cache.CompleteResourceDeletion(GpuResourceKind.Sampler, sampler);
    }
    #endregion
}

/// <summary>Retains kind-specific deletion outcome reads at the native boundary.</summary>
internal static partial class OpenGLImageDeletionCalls
{
    #region Internal API
    /// <summary>Distinguishes actual deletion from rejected native commands before retiring original registrations.</summary>
    internal static bool WasDeleted(GpuResourceKind kind, int id) => kind switch
    {
        GpuResourceKind.Texture => !GL.IsTexture(id), GpuResourceKind.Renderbuffer => !GL.IsRenderbuffer(id),
        GpuResourceKind.Sampler => !OpenGLDriverCalls.IsSampler(id), _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion
}
