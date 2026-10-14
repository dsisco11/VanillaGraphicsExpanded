using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Rebuilds engine render implementations and higher callers after native state routing.</summary>
internal static class EngineRenderApiStatePatches
{
    #region Public API
    /// <summary>Recompiles managed engine callers using the state transpiler to replace preexisting inlined native calls.</summary>
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

    /// <summary>Selects render API implementations and the explicitly identified higher engine caller classes.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static;
        var engine = typeof(ClientPlatformWindows).Assembly;
        var selected = new HashSet<MethodBase>();
        // Retain the established API hierarchy and add only the known GUI/platform caller classes.
        // Resolve internal game types by their exact names rather than broad namespace or IL scans.
        var types = engine.GetTypes().Where(type => typeof(IRenderAPI).IsAssignableFrom(type))
            .Concat(new[]
            {
                engine.GetType("Vintagestory.Client.GuiScreenConnectingToServer", throwOnError: true)!,
                engine.GetType("Vintagestory.Client.ScreenManager", throwOnError: true)!,
                engine.GetType("Vintagestory.Client.GuiCompositeMainMenuLeft", throwOnError: true)!,
                engine.GetType("Vintagestory.Client.ParticleRenderer2D", throwOnError: true)!,
                engine.GetType("Vintagestory.Client.NoObf.TextureAtlasManager", throwOnError: true)!,
                engine.GetType("Vintagestory.Client.NoObf.BlendedTextureManager", throwOnError: true)!,
                typeof(GuiComposer),
                typeof(GuiElement).Assembly.GetType("Vintagestory.API.Client.GuiElementClip", throwOnError: true)!
            }).Distinct();
        foreach (var type in types)
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>()
                .Concat(type.GetConstructors(flags & ~BindingFlags.Static)))
            {
                // Harmony rebuilds eligible bodies; static initialization and open generic bodies stay excluded.
                if (!method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null)
                    if (selected.Add(method)) yield return method;
            }
        }

        // These installed callers invoke small platform state helpers directly, bypassing
        // IRenderAPI. Rebuild both enabling and disabling paths after patching those helpers:
        // a previously inlined raw enable can otherwise leave cached-disabled knowledge stale.
        // Keep the audited methods explicit; do not discover a transitive runtime call graph.
        var loadFrameBuffer = AccessTools.DeclaredMethod(typeof(ClientPlatformWindows), "LoadFrameBuffer", [typeof(EnumFrameBuffer)])
            ?? throw new MissingMethodException(typeof(ClientPlatformWindows).FullName, "LoadFrameBuffer(EnumFrameBuffer)");
        if (selected.Add(loadFrameBuffer)) yield return loadFrameBuffer;
        foreach (var (typeName, methodName, parameters) in new[]
        {
            ("Vintagestory.Client.SystemClientCommands", "OnRenderBlockItemPngs", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.ClientMain", "Start", Type.EmptyTypes),
            ("Vintagestory.Client.NoObf.ClientMain", "MainRenderLoop", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.TextureAtlas", "Export", new[] { typeof(string), typeof(ClientMain), typeof(int) }),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderShadow", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderOpaque", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderAfterOIT", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.InventoryItemRenderer", "CreateFrameBuffer", new[] { typeof(Vintagestory.API.MathTools.Size2i), typeof(int) }),
            ("Vintagestory.Client.NoObf.SystemRenderDecals", "OnRenderFrame3D", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.SystemRenderEntities", "OnRenderOpaque3D", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.SystemRenderEntities", "OnRenderAfterOIT", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.SystemRenderInsideBlock", "OnRenderFrame3D", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.SystemRenderSunMoon", "OnRenderFrame3D", new[] { typeof(float) }),
            ("Vintagestory.Client.NoObf.SystemRenderSunMoon", "OnRenderFrame3DPost", new[] { typeof(float) })
        })
        {
            var type = engine.GetType(typeName, throwOnError: true)!;
            var method = AccessTools.DeclaredMethod(type, methodName, parameters)
                ?? throw new MissingMethodException(typeName, methodName);
            if (selected.Add(method)) yield return method;
        }
    }
    #endregion
}
