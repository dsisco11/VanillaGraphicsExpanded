using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Component.Terrain;

/// <summary>Checks terrain draw dispatch and policy restoration as a composed subsystem.</summary>
[Collection("CPU state")]
public sealed class TerrainPassDrawTests
{
    #region Public API
    #region Draw dispatch
    /// <summary>Dynamic indices and additional passes execute without replacing the selected pool or operands.</summary>
    [Fact]
    public void DynamicIndicesAndAdditionalPassesExecuteUnchanged()
    {
        // Arrange synthetic renderer IL with six selections, exceeding either installed method's old fixed count.
        var method = new DynamicMethod("selectPass", typeof(int), [typeof(Vintagestory.Client.NoObf.ChunkRenderer), typeof(int)], typeof(TerrainPassDrawTests), true);
        var il = method.GetILGenerator();
        var table = AccessTools.Field(typeof(Vintagestory.Client.NoObf.ChunkRenderer), "poolsByRenderPass");
        var render = AccessTools.Method(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render));
        var dispatch = AccessTools.Method(typeof(TerrainPassDrawHook), nameof(TerrainPassDrawHook.Render));
        var code = new List<CodeInstruction>();
        for (int i = 0; i < 6; i++)
            code.AddRange([new(OpCodes.Ldarg_0), new(OpCodes.Ldfld, table), new(OpCodes.Nop), new(OpCodes.Ldarg_1),
                new(OpCodes.Ldelem_Ref), new(OpCodes.Ldc_I4_0), new(OpCodes.Ldelem_Ref),
                new(OpCodes.Ldnull), new(OpCodes.Ldstr, "origin"), new(OpCodes.Ldc_I4_1), new(OpCodes.Callvirt, render)]);

        // Act by transpiling the synthetic draw selections and emitting the selected pass to a recorder.
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

        // Assert all six selected indices survive, then reject an ambiguous branch layout.
        foreach (int pass in new[] { 8, 1, 5, 0, 2, 4 }) Assert.Equal(pass, execute(renderer, pass));
        var ambiguous = code.ToList();
        ambiguous.Insert(7, new CodeInstruction(OpCodes.Br_S, il.DefineLabel()));
        Assert.Throws<InvalidOperationException>(() => TerrainPassDrawHook.Transpiler(ambiguous, il).ToArray());
    }

    /// <summary>A failing engine draw restores the enclosing displacement eligibility value.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExceptionalDrawRestoresPolicy(bool previous)
    {
        // Arrange a known ambient value and retain it for unconditional cleanup.
        bool saved = TerrainDisplacementRuntime.EligiblePool;
        try
        {
            TerrainDisplacementRuntime.EligiblePool = previous;

            // Act through the production draw entry with a null manager to force its failure path.
            Assert.Throws<NullReferenceException>(() => TerrainPassDrawHook.Render(null!, new(), "origin", EnumFrustumCullMode.CullNormal, EnumChunkRenderPass.TopSoil));

            // Assert the render scope restored the exact enclosing policy before finally cleanup.
            Assert.Equal(previous, TerrainDisplacementRuntime.EligiblePool);
        }
        finally { TerrainDisplacementRuntime.EligiblePool = saved; }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Returns the captured category while checking the unchanged engine draw operands.</summary>
    private static int CapturePass(MeshDataPoolManager manager, Vintagestory.API.MathTools.Vec3d position,
        string origin, EnumFrustumCullMode culling, EnumChunkRenderPass pass)
    {
        Assert.Equal("origin", origin);
        Assert.Equal(1, (int)culling);
        return (int)pass;
    }
    #endregion
}
