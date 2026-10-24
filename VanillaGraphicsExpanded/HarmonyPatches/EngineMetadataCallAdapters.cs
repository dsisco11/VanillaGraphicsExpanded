using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Resolves compiler-checked metadata adapters by exact native method identity.</summary>
internal static class EngineMetadataCallAdapters
{
    private static readonly IReadOnlyDictionary<MethodInfo, MethodInfo> definitions = CreateMappings();
    private static readonly ConcurrentDictionary<MethodInfo, MethodInfo> constructed = new();

    #region Internal API
    /// <summary>Recognizes explicitly mapped signatures, including generic definitions used during discovery.</summary>
    internal static bool Supports(MethodInfo native)
    {
        var definition = native.IsGenericMethod ? native.GetGenericMethodDefinition() : native;
        return definitions.ContainsKey(definition);
    }

    /// <summary>Closes an ordinary generic C# adapter over the caller's exact payload types.</summary>
    internal static bool TryGet(MethodInfo native, out MethodInfo replacement)
    {
        replacement = null!;
        if (native.ContainsGenericParameters) return false;
        if (!native.IsGenericMethod) return definitions.TryGetValue(native, out replacement!);
        if (!definitions.TryGetValue(native.GetGenericMethodDefinition(), out var definition)) return false;

        // Generic closure preserves the compiler-checked body and constraints. It does
        // not generate IL, interpret command names, or restrict the caller's payload type.
        replacement = constructed.GetOrAdd(native,
            static (method, adapter) => adapter.MakeGenericMethod(method.GetGenericArguments()), definition);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Validates explicit native-owner declarations once before any engine call is rewritten.</summary>
    private static IReadOnlyDictionary<MethodInfo, MethodInfo> CreateMappings()
    {
        var mappings = new Dictionary<MethodInfo, MethodInfo>();
        MetadataAdapterCatalog.Register(mappings);
        return new ReadOnlyDictionary<MethodInfo, MethodInfo>(mappings);
    }
    #endregion
}
