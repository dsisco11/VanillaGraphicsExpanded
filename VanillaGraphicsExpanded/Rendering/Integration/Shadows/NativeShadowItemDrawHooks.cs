using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Preserves native item drawing while avoiding a second pose advancement after early preparation.</summary>
internal static class NativeShadowItemDrawHooks
{
    private static readonly Action<EntityItemRenderer,ItemRenderInfo,bool,float> Load=
        AccessTools.MethodDelegate<Action<EntityItemRenderer,ItemRenderInfo,bool,float>>(AccessTools.Method(typeof(EntityItemRenderer),"LoadModelMatrix"));

    #region Public API
    /// <summary>Guards only the qualified native interpolation block and routes its one subsequent model-matrix call.</summary>
    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source,ILGenerator generator)
    {
        var code=source.Select(instruction=>new CodeInstruction(instruction)).ToList();
        var position=AccessTools.Field(typeof(EntityItemRenderer),"lerpedPos");
        var x=AccessTools.Field(typeof(Vec3d),nameof(Vec3d.X));var y=AccessTools.Field(typeof(Vec3d),nameof(Vec3d.Y));var z=AccessTools.Field(typeof(Vec3d),nameof(Vec3d.Z));
        var load=AccessTools.Method(typeof(EntityItemRenderer),"LoadModelMatrix");
        var starts=new List<int>();
        for(int i=0;i+3<code.Count;i++)
            if(code[i].opcode==OpCodes.Ldarg_0 && code[i+1].LoadsField(position) && code[i+2].opcode==OpCodes.Dup && code[i+3].LoadsField(x))starts.Add(i);
        if(starts.Count!=1 || code.Count(i=>i.Calls(load))!=1)throw new NotSupportedException("Native dropped-item pose structure changed.");
        int start=starts[0],end=code.FindIndex(start,instruction=>instruction.StoresField(z));
        if(end<start || end+1>=code.Count)throw new NotSupportedException("Native dropped-item interpolation is incomplete.");
        var block=code.GetRange(start,end-start+1);
        if(block.Count(i=>i.StoresField(x))!=1 || block.Count(i=>i.StoresField(y))!=1 || block.Count(i=>i.StoresField(z))!=1 ||
            block.Count(i=>i.opcode==OpCodes.Ldc_R8 && Equals(i.operand,22d))!=3 || block.Any(i=>i.blocks.Count!=0))
            throw new NotSupportedException("Native dropped-item interpolation no longer matches the qualified owner.");
        var skip=generator.DefineLabel();code[end+1].labels.Add(skip);
        var receiver=new CodeInstruction(OpCodes.Ldarg_0);
        receiver.labels.AddRange(code[start].labels);code[start].labels.Clear();
        foreach(var instruction in code)
        {
            if(ReferenceEquals(instruction,code[start]))
            {
                yield return receiver;
                yield return new(OpCodes.Call,AccessTools.Method(typeof(NativeShadowEntityHooks),nameof(NativeShadowEntityHooks.ItemPrepared)));
                yield return new(OpCodes.Brtrue,skip);
            }
            if(instruction.Calls(load)) { instruction.opcode=OpCodes.Call;instruction.operand=AccessTools.Method(typeof(NativeShadowItemDrawHooks),nameof(LoadForDraw)); }
            yield return instruction;
        }
    }

    /// <summary>Rebuilds the current visible matrix using already advanced state, or preserves unprepared native behavior.</summary>
    internal static void LoadForDraw(EntityItemRenderer renderer,ItemRenderInfo info,bool shadowPass,float dt)
    {
        bool prepared=NativeShadowEntityHooks.ItemPrepared(renderer);
        Load(renderer,info,shadowPass||prepared,prepared?0:dt);
    }
    #endregion
}
