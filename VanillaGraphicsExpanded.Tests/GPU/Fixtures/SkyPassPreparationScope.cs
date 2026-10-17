using HarmonyLib;
using System.Reflection.Emit;
using System.Reflection;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Replaces only pass preparation in a scheduler test while retaining the actual owning renderer and GPU draws.</summary>
internal sealed class SkyPassPreparationScope : IDisposable
{
    [ThreadStatic] private static SkyPassPreparationScope? active;
    private readonly Harmony harmony = new("Tests.SingleSky.PreparedPasses");
    private readonly SkyDraw[] draws;
    private static readonly Type[] Types = [typeof(CelestialDrawPass), typeof(MoonDrawPass), typeof(SunDrawPass), typeof(AtmosphereSkyPass)];
    internal readonly List<string> Calls = [];

    #region Public API
    /// <summary>Retains registration and scheduler control flow while intercepting its four optional preparation calls.</summary>
    internal SkyPassPreparationScope(SkyDraw[] draws)
    {
        if (active is not null) throw new InvalidOperationException("Prepared pass interception is already active.");
        if (draws.Length != Types.Length) throw new ArgumentException("Four prepared draws are required.", nameof(draws));
        this.draws = draws;
        active = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(SkyRenderer), nameof(SkyRenderer.OnRenderFrame)),
                transpiler: new HarmonyMethod(AccessTools.Method(typeof(SkyPassPreparationScope), nameof(ReplacePreparation))));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Removes all interception after success or failure so later world-admission checks run original preparation.</summary>
    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion

    #region Private
    /// <summary>Returns the matching complete GPU workload and records the real renderer's invocation order.</summary>
    private static SkyDraw Prepare(object instance)
    {
        var scope = active ?? throw new InvalidOperationException("Prepared pass interception is not active.");
        int index = Array.IndexOf(Types, instance.GetType());
        scope.Calls.Add(Types[index].Name);
        return scope.draws[index];
    }

    /// <summary>Replaces only preparation calls in the actual scheduler, leaving its order, stage checks and shared submission intact.</summary>
    private static IEnumerable<CodeInstruction> ReplacePreparation(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToArray();
        int count = 0;
        foreach (var instruction in code)
        {
            if (instruction.operand is not MethodInfo method || method.Name != "PrepareDraw"
                || !Types.Contains(method.DeclaringType)) continue;
            // Patch the call seam instead of rebuilding a callee's exception-filter regions.
            instruction.opcode = OpCodes.Call;
            instruction.operand = AccessTools.Method(typeof(SkyPassPreparationScope), nameof(Prepare));
            count++;
        }
        Assert.Equal(4, count);
        return code;
    }
    #endregion
}
