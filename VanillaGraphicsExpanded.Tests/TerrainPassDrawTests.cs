using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies engine-supplied pass policy and exact terrain draw rewriting.</summary>
[Collection("CPU state")]
public sealed class TerrainPassDrawTests
{
    private static int nesting;
    private static readonly List<bool> observed = [];
    #region Public API
    /// <summary>Additional and reordered passes use evaluated argument indices instead of a fixed sequence.</summary>
    [Fact]
    public void DynamicIndicesAndAdditionalPassesExecuteUnchanged()
    {
        var method = new DynamicMethod("selectPass", typeof(int), [typeof(Vintagestory.Client.NoObf.ChunkRenderer), typeof(int)], typeof(TerrainPassDrawTests), true);
        var il = method.GetILGenerator();
        var table = AccessTools.Field(typeof(Vintagestory.Client.NoObf.ChunkRenderer), "poolsByRenderPass");
        var render = AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render));
        var dispatch = AccessTools.Method(typeof(TerrainPassDrawHook), nameof(TerrainPassDrawHook.Render));
        var code = new List<CodeInstruction>();
        // Six selections exceed either installed method's old fixed count.
        for (int i = 0; i < 6; i++)
            code.AddRange([new(OpCodes.Ldarg_0), new(OpCodes.Ldfld, table), new(OpCodes.Nop), new(OpCodes.Ldarg_1),
                new(OpCodes.Ldelem_Ref), new(OpCodes.Ldc_I4_0), new(OpCodes.Ldelem_Ref),
                new(OpCodes.Ldnull), new(OpCodes.Ldstr, "origin"), new(OpCodes.Ldc_I4_1), new(OpCodes.Callvirt, render)]);
        var rewritten = TerrainPassDrawHook.Transpiler(code, il).ToArray();
        int calls = 0;
        foreach (var instruction in rewritten)
        {
            if (instruction.Calls(dispatch))
            {
                il.Emit(OpCodes.Call, AccessTools.Method(typeof(TerrainPassDrawTests), nameof(CapturePass)));
                if (++calls < 6) il.Emit(OpCodes.Pop);
            }
            else if (instruction.operand is System.Reflection.FieldInfo field) il.Emit(instruction.opcode, field);
            else if (instruction.operand is LocalBuilder local) il.Emit(instruction.opcode, local);
            else if (instruction.operand is string text) il.Emit(instruction.opcode, text);
            else il.Emit(instruction.opcode);
        }
        il.Emit(OpCodes.Ret);
        var execute = method.CreateDelegate<Func<Vintagestory.Client.NoObf.ChunkRenderer, int, int>>();
        var renderer = (Vintagestory.Client.NoObf.ChunkRenderer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Vintagestory.Client.NoObf.ChunkRenderer));
        renderer.poolsByRenderPass = Enumerable.Range(0, 9).Select(_ => new MeshDataPoolManager[1]).ToArray();
        foreach (int pass in new[] { 8, 1, 5, 0, 2, 4 }) Assert.Equal(pass, execute(renderer, pass));
        var ambiguous = code.ToList();
        ambiguous.Insert(7, new CodeInstruction(OpCodes.Br_S, il.DefineLabel()));
        Assert.Throws<InvalidOperationException>(() => TerrainPassDrawHook.Transpiler(ambiguous, il).ToArray());
    }

    /// <summary>Every category has independent two-sided and displacement policy without manager identity.</summary>
    [Theory]
    [InlineData(EnumChunkRenderPass.Opaque, false, true)]
    [InlineData(EnumChunkRenderPass.TopSoil, false, true)]
    [InlineData(EnumChunkRenderPass.OpaqueNoCull, true, false)]
    [InlineData(EnumChunkRenderPass.BlendNoCull, true, false)]
    [InlineData(EnumChunkRenderPass.Liquid, false, false)]
    [InlineData(EnumChunkRenderPass.Transparent, false, false)]
    [InlineData(EnumChunkRenderPass.Meta, false, false)]
    [InlineData(EnumChunkRenderPass.OpaqueWaterPlant, false, false)]
    [InlineData(EnumChunkRenderPass.Decor, false, false)]
    public void CategoryPolicies(EnumChunkRenderPass pass, bool twoSided, bool displacement)
    {
        Assert.Equal(twoSided, TerrainSurfaceNormals.IsTwoSided(pass));
        Assert.Equal(displacement, TerrainDisplacementRuntime.IsEligiblePass(pass));
    }

    /// <summary>Installed opaque and shadow draws retain every original operand and unrelated call.</summary>
    [Theory]
    [InlineData("RenderOpaque", 5)]
    [InlineData("RenderShadow", 4)]
    public void InstalledSelections(string name, int count)
    {
        var method = TerrainPassDrawHook.TargetMethods().Single(m => m.Name == name);
        var il = new DynamicMethod("passLayout", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, il).ToArray();
        var rewritten = TerrainPassDrawHook.Transpiler(original, il).ToArray();
        var render = AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render));
        var dispatch = AccessTools.Method(typeof(TerrainPassDrawHook), nameof(TerrainPassDrawHook.Render));
        Assert.Equal(count, rewritten.Count(i => i.Calls(dispatch)));
        Assert.All(original.Where(i => !i.Calls(render)), instruction => Assert.Contains(instruction, rewritten));
        Assert.Throws<InvalidOperationException>(() => TerrainPassDrawHook.Transpiler(original.Where(i => !i.Calls(render)), il).ToArray());
    }

    /// <summary>Even a failing engine call restores enclosing displacement policy.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExceptionalDrawRestoresPolicy(bool previous)
    {
        bool saved = TerrainDisplacementRuntime.EligiblePool;
        try
        {
            TerrainDisplacementRuntime.EligiblePool = previous;
            Assert.Throws<NullReferenceException>(() => TerrainPassDrawHook.Render(null!, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.TopSoil));
            Assert.Equal(previous, TerrainDisplacementRuntime.EligiblePool);
        }
        finally { TerrainDisplacementRuntime.EligiblePool = saved; }
    }

    /// <summary>Real Harmony installation accepts engine IL, and nested manager draws restore enclosing policy.</summary>
    [Fact]
    public void InstalledHookAndNestedScopes()
    {
        var harmony = new Harmony("VGE.Tests.TerrainPassScopes");
        bool previous = TerrainDisplacementRuntime.EligiblePool;
        try
        {
            harmony.CreateClassProcessor(typeof(TerrainPassDrawHook)).Patch();
            harmony.CreateClassProcessor(typeof(TerrainDisplacementMovingPoolScope)).Patch();
            harmony.Patch(AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render)),
                prefix: new HarmonyMethod(typeof(TerrainPassDrawTests), nameof(NestedManager)));
            observed.Clear(); nesting = 0;
            TerrainDisplacementRuntime.EligiblePool = false;
            var manager = (MeshDataPoolManager)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MeshDataPoolManager));
            TerrainPassDrawHook.Render(manager, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.Opaque);
            Assert.Equal(new[] { true, false, true }, observed);
            Assert.False(TerrainDisplacementRuntime.EligiblePool);
            bool moving = TerrainDisplacementRuntime.MovingPool;
            TerrainDisplacementMovingPoolScope.Prefix(Vintagestory.API.Config.Dimensions.MiniDimensions, out var saved);
            Assert.True(TerrainDisplacementRuntime.MovingPool);
            TerrainDisplacementMovingPoolScope.Finalizer(saved);
            Assert.Equal(moving, TerrainDisplacementRuntime.MovingPool);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            TerrainDisplacementRuntime.EligiblePool = previous;
        }
    }
    #endregion

    #region Private
    /// <summary>Returns the captured category while consuming the unchanged engine draw operands.</summary>
    private static int CapturePass(MeshDataPoolManager manager, Vintagestory.API.MathTools.Vec3d position,
        string origin, EnumFrustumCullMode culling, EnumChunkRenderPass pass)
    {
        Assert.Equal("origin", origin);
        Assert.Equal(1, (int)culling);
        return (int)pass;
    }

    /// <summary>Substitutes only world-dependent manager work while exercising the production draw scope.</summary>
    private static bool NestedManager(MeshDataPoolManager __instance)
    {
        observed.Add(TerrainDisplacementRuntime.EligiblePool);
        if (nesting++ == 0)
        {
            TerrainPassDrawHook.Render(__instance, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.BlendNoCull);
            observed.Add(TerrainDisplacementRuntime.EligiblePool);
        }
        return false;
    }
    #endregion
}
