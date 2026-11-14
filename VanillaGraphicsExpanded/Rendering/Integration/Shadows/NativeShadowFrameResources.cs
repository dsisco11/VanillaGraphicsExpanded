using System;
using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Owns one drained configuration generation's native preparation hooks and authored shadow executables.</summary>
internal sealed class NativeShadowFrameResources : IDisposable
{
    private readonly Harmony hooks=new(Constants.ModId+".shadowgeneration");
    private readonly List<IDisposable> shaders=[];
    private readonly ShadowNativeMaterialShader?[,] terrain=new ShadowNativeMaterialShader?[2,2];
    private bool installed,disposed;

    #region Public API
    internal NativeShadowEntityPreparation Preparation { get; }
    internal ShadowSceneChanges Changes { get; }
    internal ShadowEntityShader Entities { get; private set; }=null!;
    internal ShadowStaticShader Items { get; private set; }=null!;
    internal bool Waving { get; }

    /// <summary>Prepares the native owner's active layout through packaged SPIR-V, without requiring unused vertex storage-block support.</summary>
    internal NativeShadowFrameResources(ICoreClientAPI api,ClientMain game,ShadowSettings settings,bool storage)
    {
        Changes=new(settings.Inputs.ChangeCapacity);
        Preparation=new(settings.Drawing.EntityVisits,Changes);
        Waving=ClientSettings.WavingFoliage;
        try
        {
            int layout=storage?1:0;
            terrain[layout,0]=Own(ShadowTerrainShader.Create(api,storage,Waving));
            terrain[layout,1]=Own(ShadowDisplacedTerrainShader.Create(api,storage,Waving));
            Entities=Own(ShadowEntityShader.Create(api,Waving));Items=Own(ShadowStaticShader.Create(api,false,Waving));
            NativeShadowEntityHooks.Install(hooks,game,Preparation);installed=true;
        }
        catch{Dispose();throw;}
    }

    /// <summary>Selects the exact native storage and displacement producer without compiling source during drawing.</summary>
    internal ShadowNativeMaterialShader Terrain(bool storage,bool displaced)=>terrain[storage?1:0,displaced?1:0]??
        throw new InvalidOperationException("The native terrain layout changed after shadow generation creation.");

    /// <summary>Ends this generation's callbacks only after its gate and source have relinquished submitted borrows.</summary>
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(installed){NativeShadowEntityHooks.Detach();installed=false;}
        var release=new List<Action>{()=>hooks.UnpatchAll(hooks.Id)};
        foreach(var shader in shaders)release.Add(shader.Dispose);
        shaders.Clear();RenderingCleanup.Run(release.ToArray());
    }
    #endregion

    #region Private
    /// <summary>Records only successfully created executables so partial construction is exception-safe.</summary>
    private T Own<T>(T shader) where T:IDisposable{shaders.Add(shader);return shader;}
    #endregion
}
