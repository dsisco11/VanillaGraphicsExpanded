using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Associates a post-call observer with an audited native OpenGL entrypoint.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class InterceptOpenGLAttribute : Attribute
{
    #region Public API
    /// <summary>Names the native symbol whose arguments and optional result the observer receives.</summary>
    public InterceptOpenGLAttribute(string entrypoint) { EntryPoint = entrypoint; }

    /// <summary>Gets the native symbol resolved against the generator's selected ABI inventory.</summary>
    public string EntryPoint { get; }
    #endregion
}
