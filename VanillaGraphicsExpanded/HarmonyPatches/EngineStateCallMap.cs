using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Matches native state operations to adapters with exactly the same managed calling signature.</summary>
internal static class EngineStateCallMap
{
    private static readonly IReadOnlyDictionary<MethodInfo, MethodInfo> replacements = Create();

    #region Public API
    /// <summary>Exposes the audited native-to-cache mapping for discovery and coverage verification.</summary>
    internal static IReadOnlyDictionary<MethodInfo, MethodInfo> Replacements => replacements;

    /// <summary>Resolves fixed adapters and exact closed metadata overloads through the same call-site boundary.</summary>
    internal static bool TryGetReplacement(MethodInfo native, out MethodInfo replacement) =>
        replacements.TryGetValue(native, out replacement!) || EngineMetadataCallAdapters.TryGet(native, out replacement);

    /// <summary>Recognizes generic definitions for discovery without inventing a fixed set of upload element types.</summary>
    internal static bool Supports(MethodInfo native) => replacements.ContainsKey(native) || EngineMetadataCallAdapters.Supports(native);
    #endregion

    #region Private
    /// <summary>Rejects adapter drift instead of installing a stack-incompatible replacement.</summary>
    private static IReadOnlyDictionary<MethodInfo, MethodInfo> Create()
    {
        var result = new Dictionary<MethodInfo, MethodInfo>();
        foreach (var adapter in typeof(EngineStateCalls).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var parameters = adapter.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var native = typeof(GL).GetMethod(adapter.Name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
            if (native is null || native.ReturnType != adapter.ReturnType)
                throw new InvalidOperationException($"Unsupported engine state adapter signature: {adapter}.");
            result.Add(native, adapter);
        }
        foreach (var pair in new (Type Native, Type Adapter)[]
        {
            (typeof(GL), typeof(EngineStateDeletionCalls.Core)),
            (typeof(GL.Arb), typeof(EngineStateDeletionCalls.Arb)),
            (typeof(GL.Ext), typeof(EngineStateDeletionCalls.Ext)),
            (typeof(GL), typeof(Rendering.RHI.Backends.OpenGL.OpenGLImageDeletionCalls.Core)),
            (typeof(GL.Ext), typeof(Rendering.RHI.Backends.OpenGL.OpenGLImageDeletionCalls.Ext)),
            (typeof(GL), typeof(Rendering.RHI.Backends.OpenGL.OpenGLBufferDeletionCalls.Core)),
            (typeof(GL.Arb), typeof(Rendering.RHI.Backends.OpenGL.OpenGLBufferDeletionCalls.Arb)),
        })
        {
            foreach (var adapter in pair.Adapter.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var parameters = adapter.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                var native = pair.Native.GetMethod(adapter.Name, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                if (native is null || native.ReturnType != adapter.ReturnType)
                    throw new InvalidOperationException($"Unsupported native deletion adapter signature: {adapter}.");
                result.TryAdd(native, adapter);
            }
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<MethodInfo, MethodInfo>(result);
    }
    #endregion
}
