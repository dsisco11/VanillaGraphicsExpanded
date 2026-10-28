using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OpenTK;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Keeps native observation attached to both generated OpenTK tables across binding reloads.</summary>
[HarmonyPatch]
internal static class OpenGLBindingLoaderHook
{
    #region Internal API
    /// <summary>Selects the two loader methods rather than scanning or recompiling their callers.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return typeof(OpenTK.Graphics.OpenGL.GL).GetMethod("LoadBindings", [typeof(IBindingsContext)])!;
        yield return typeof(OpenTK.Graphics.OpenGL4.GL).GetMethod("LoadBindings", [typeof(IBindingsContext)])!;
    }

    /// <summary>Preserves the loader's resolution policy while substituting callbacks at pointer publication.</summary>
    [HarmonyPrefix]
    internal static void Prefix(ref IBindingsContext context)
        => context = OpenGLBindingInterception.ObserveLoader(context);

    /// <summary>Reconciles owned entries even when a resolver failed halfway through a binding load.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(MethodBase __originalMethod)
        => OpenGLBindingInterception.BindingsLoaded(__originalMethod.DeclaringType!);
    #endregion
}
