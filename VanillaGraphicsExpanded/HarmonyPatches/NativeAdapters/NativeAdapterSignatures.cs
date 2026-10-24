using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace VanillaGraphicsExpanded.HarmonyPatches.NativeAdapters;

/// <summary>Validates explicit C# adapters against their declared OpenTK owner and exact signatures.</summary>
internal static class NativeAdapterSignatures
{
    #region Internal API
    /// <summary>Rejects ambiguous, absent or constraint-incompatible native signatures before patch installation.</summary>
    internal static void Register(Dictionary<MethodInfo, MethodInfo> mappings, Type nativeOwner, Type adapterOwner)
    {
        var natives = nativeOwner.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        foreach (var adapter in adapterOwner.GetMethods(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            // C# checks each native call in the adapter body; this check additionally
            // guarantees that replacing the caller's operand preserves its managed stack.
            var matches = natives.Where(native => Matches(native, adapter)).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected one exact native signature for {adapterOwner.FullName}.{adapter}; found {matches.Length}.");
            if (!mappings.TryAdd(matches[0], adapter))
                throw new InvalidOperationException($"Duplicate metadata adapter for {matches[0]}.");
        }
    }
    #endregion

    #region Private
    /// <summary>Matches names and structural signatures without inferring native command semantics.</summary>
    private static bool Matches(MethodInfo native, MethodInfo adapter)
    {
        if (native.Name != adapter.Name || !SameType(native.ReturnType, adapter.ReturnType)) return false;
        var nativeArguments = native.GetGenericArguments();
        var adapterArguments = adapter.GetGenericArguments();
        if (nativeArguments.Length != adapterArguments.Length) return false;
        var nativeParameters = native.GetParameters();
        var adapterParameters = adapter.GetParameters();
        if (nativeParameters.Length != adapterParameters.Length) return false;
        for (int index = 0; index < nativeParameters.Length; index++)
            if (!SameType(nativeParameters[index].ParameterType, adapterParameters[index].ParameterType)) return false;

        // Generic contracts must match for every legal payload, not only current callers.
        for (int index = 0; index < nativeArguments.Length; index++)
        {
            var expected = nativeArguments[index];
            var actual = adapterArguments[index];
            if (expected.GenericParameterAttributes != actual.GenericParameterAttributes) return false;
            var expectedConstraints = expected.GetGenericParameterConstraints();
            var actualConstraints = actual.GetGenericParameterConstraints();
            if (expectedConstraints.Length != actualConstraints.Length
                || expectedConstraints.Any(constraint => !actualConstraints.Any(other => SameType(constraint, other)))) return false;
        }
        return true;
    }

    /// <summary>Compares generic positions and exact element shapes across independently declared methods.</summary>
    private static bool SameType(Type native, Type adapter)
    {
        if (native == adapter) return true;
        if (native.IsGenericParameter || adapter.IsGenericParameter)
            return native.IsGenericParameter && adapter.IsGenericParameter
                && native.DeclaringMethod is not null && adapter.DeclaringMethod is not null
                && native.GenericParameterPosition == adapter.GenericParameterPosition;
        if (native.HasElementType || adapter.HasElementType)
            return native.HasElementType && adapter.HasElementType
                && native.IsByRef == adapter.IsByRef && native.IsPointer == adapter.IsPointer
                && native.IsArray == adapter.IsArray
                && (!native.IsArray || native.GetArrayRank() == adapter.GetArrayRank())
                && SameType(native.GetElementType()!, adapter.GetElementType()!);
        if (!native.IsGenericType || !adapter.IsGenericType
            || native.GetGenericTypeDefinition() != adapter.GetGenericTypeDefinition()) return false;
        var nativeArguments = native.GetGenericArguments();
        var adapterArguments = adapter.GetGenericArguments();
        return nativeArguments.Length == adapterArguments.Length
            && nativeArguments.Zip(adapterArguments).All(pair => SameType(pair.First, pair.Second));
    }
    #endregion
}
