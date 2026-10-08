using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Rebuilds engine render API bodies after native state call sites have been patched.</summary>
internal static class EngineRenderApiStatePatches
{
    #region Public API
    /// <summary>Removes preexisting inlined platform calls from render API implementations using the state transpiler.</summary>
    internal static void Apply(Harmony harmony)
    {
        // The raw-call patches must already exist before Harmony compiles these replacement bodies.
        // Reusing the transpiler also routes any direct mapped GL calls in an API implementation.
        var transpiler = new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler));
        foreach (var method in TargetMethods())
        {
            // A direct GL caller may already have received this transpiler during PatchAll.
            // Rebuild it after all callees are patched without registering the same transpiler twice.
            var patches = Harmony.GetPatchInfo(method);
            if (patches?.Transpilers.Any(patch => patch.owner == harmony.Id && patch.PatchMethod == transpiler.method) == true)
                harmony.CreateProcessor(method).Patch();
            else
                harmony.Patch(method, transpiler: transpiler);
        }
    }

    /// <summary>Selects declared managed bodies by the engine render API contract, without individual method names.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static;
        // Limit discovery to the engine: third-party implementations and dynamically emitted code are outside this boundary.
        foreach (var type in typeof(ClientPlatformWindows).Assembly.GetTypes())
        {
            if (!typeof(IRenderAPI).IsAssignableFrom(type) || type.ContainsGenericParameters)
                continue;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>()
                .Concat(type.GetConstructors(flags & ~BindingFlags.Static)))
            {
                // Static initializers are intentionally excluded; abstract/native/open generic bodies cannot be rebuilt here.
                if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null)
                    yield return method;
            }
        }
    }
    #endregion
}
