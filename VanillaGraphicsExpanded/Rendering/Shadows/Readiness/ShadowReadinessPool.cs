using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>Publishes GPU-only whole-light eligibility before surface consumers, with no completion readback or CPU wait.</summary>
internal sealed class ShadowReadinessPool:IDisposable
{
    private readonly ShadowReadinessShader shader;
    private readonly RHIShaderRef executable;
    private readonly GpuShaderStorageBuffer inputs,history;
    private readonly ShadowSchedulingSettings policy;
    private readonly ShadowTemporalSettings temporal;
    private readonly uint[] packed;
    private bool disposed;
    private long frame;
    private readonly Dictionary<Lights.ShadowSourceKey,int> historySlots=new();
    internal GpuShaderStorageBuffer Results { get; }
    internal GpuShaderStorageBuffer Priority { get; }
    /// <summary>Read-only consumer binding for the projection/request interval selected by GPU readiness.</summary>
    internal GpuShaderStorageBuffer Requests=>inputs;
    internal long AllocatedBytes { get; }
    internal long Frame => frame;

    #region Public API
    /// <summary>Allocates fixed bounded demand, status and history storage against actual baseline SSBO limits.</summary>
    internal ShadowReadinessPool(ICoreAPI api,ShadowSchedulingSettings settings,ShadowTemporalSettings temporal)
    {
        policy=settings.Freeze();this.temporal=temporal.Freeze();
        long inputBytes=checked(64+policy.LightCapacity*128L+policy.DemandRequests*640L);
        AllocatedBytes=checked(inputBytes*2+policy.LightCapacity*528L);
        var caps=GpuSupport.Graphics;
        if(inputBytes>int.MaxValue || inputBytes>caps.MaxShaderStorageBlockSize || caps.MaxComputeShaderStorageBlocks<5 ||
            caps.MaxShaderStorageBufferBindings<5 || AllocatedBytes>policy.MemoryMiB*1048576L)
            throw new InvalidOperationException("Whole-light readiness storage exceeds effective capacity.");
        packed=new uint[checked((int)(inputBytes/4))];
        shader=ShadowReadinessShader.Create(api);executable=shader.Shader;
        try { inputs=Buffer((int)inputBytes,"Inputs");Results=Buffer(policy.LightCapacity*128,"Results");history=Buffer(policy.LightCapacity*128,"History");Priority=Buffer(policy.LightCapacity*16,"Priority");
            history.UploadSubData<uint>(new uint[policy.LightCapacity*32],0);Withdraw(); }
        catch { shader.Dispose();inputs?.Dispose();Results?.Dispose();history?.Dispose();Priority?.Dispose();throw; }
    }

    /// <summary>Orders page visibility and complete current demand reduction; statuses remain GPU-owned.</summary>
    internal void Publish(ShadowPagePool pool,long frame,double now,IReadOnlyList<ShadowLightDemand> lights,bool capabilitiesCurrent,
        bool unavailableOnly=false)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(frame<=this.frame || !(pool.CanSubmit || unavailableOnly&&pool.IsAvailable) || lights.Count>policy.LightCapacity || !double.IsFinite(now) || now<0 ||
            (unavailableOnly&&lights.Any(light=>light.Selected||light.Complete)))
            throw new InvalidOperationException("Readiness requires a new current frame inside the page submission interval.");
        if(!shader.EnsureReady() || shader.Shader!=executable) { Withdraw();throw new InvalidOperationException("Readiness shader changed."); }
        var words=packed;
        Array.Clear(words,0,checked(16+policy.LightCapacity*32));
        words[0]=(uint)lights.Count;words[2]=(uint)(16+policy.LightCapacity*32);words[3]=capabilitiesCurrent?1u:0u;
        ShadowPageRequest.Put(words,4,frame);words[6]=(uint)policy.RecoveryCompleteFrames;
        var keys=new HashSet<Lights.ShadowSourceKey>();int count=0;
        var active=lights.Select(light=>light.Source).ToHashSet();
        foreach(var removed in historySlots.Keys.Where(source=>!active.Contains(source)).ToArray())historySlots.Remove(removed);
        var occupied=new bool[policy.LightCapacity];
        foreach(int slot in historySlots.Values)occupied[slot]=true;
        for(int i=0;i<lights.Count;i++)
        {
            var light=lights[i];int at=16+i*32;
            if(!light.Source.IsKnown || !keys.Add(light.Source)) throw new ArgumentException("Unique known source identities required.",nameof(lights));
            ShadowPageRequest.WriteKey(words.AsSpan(at,17),new(light.Source,1,0,0,0));
            words[at+12]=(uint)count;words[at+16]=unchecked((uint)light.Detail);
            if(!historySlots.TryGetValue(light.Source,out int historySlot))
            {
                historySlot=Array.IndexOf(occupied,false);
                if(historySlot<0)throw new InvalidOperationException("Readiness source history capacity exhausted.");
                occupied[historySlot]=true;historySlots.Add(light.Source,historySlot);
            }
            words[at+20]=(uint)historySlot;
            words[at+21]=light.Contributing?1u:0u;
            bool valid=!light.Pages.IsDefault && light.Pages.Length<=policy.DemandRequests-count;
            words[at+14]=light.Complete&&valid?1u:0u;words[at+15]=light.Selected?1u:0u;
            if(valid) foreach(var page in light.Pages)
            {
                if(page.Request.Key.Source!=light.Source) throw new ArgumentException("Demand source disagrees with light.");
                page.Pack(now,temporal,pool.Capacity,pool.ProbeLimit,pool.ArrayLayers).CopyTo(words,(int)words[2]+count*160);count++;
            }
            words[at+13]=(uint)(count-(int)words[at+12]);
            words[at+18]=(uint)count;
            bool retained=!light.RetainedPages.IsDefault && light.RetainedPages.Length<=policy.DemandRequests-count;
            if(retained)foreach(var page in light.RetainedPages)
            {
                if(page.Request.Key.Source!=light.Source)throw new ArgumentException("Retained demand source disagrees with light.");
                page.Pack(now,temporal,pool.Capacity,pool.ProbeLimit,pool.ArrayLayers).CopyTo(words,(int)words[2]+count*160);count++;
            }
            words[at+19]=(uint)(count-(int)words[at+18]);
        }
        words[1]=(uint)count;
        inputs.UploadSubData<uint>(words.AsSpan(0,checked((int)words[2]+count*160)),0);
        // Clear absent slots too; a consumer cannot inherit a disappeared source from a previous frame.
        Results.UploadSubData<uint>(new uint[policy.LightCapacity*32],0);
        var binding=(IShadowReadinessBindings)shader;binding.Inputs=inputs;binding.Entries=pool.Entries;
        binding.Results=Results;binding.History=history;binding.Depth=pool.DepthArray;
        binding.Priority=Priority;
        var rhi=RHICommandContext.Current;
        using var boundary=rhi.EnterBoundary(new("Shadows.Readiness",true,compute:[shader.Pipeline]));
        boundary.Run(()=> { rhi.Barrier(new(ResourceAccess.ShaderStorage|ResourceAccess.Image|ResourceAccess.TextureFetch|ResourceAccess.BufferUpdate));
            if(lights.Count>0) shader.Dispatch(lights.Count);
            rhi.Barrier(new(ResourceAccess.ShaderStorage|ResourceAccess.TextureFetch|ResourceAccess.BufferUpdate)); });
        this.frame=frame;
    }

    /// <summary>Hard withdrawal precedes consumers and prevents recovery history from extending resource or capability loss.</summary>
    internal void Withdraw(bool resetHistory=true)
    {
        if(disposed)return;
        Results.UploadSubData<uint>(new uint[policy.LightCapacity*32],0);
        Priority.UploadSubData<uint>(new uint[policy.LightCapacity*4],0);
        if(resetHistory)
        {history.UploadSubData<uint>(new uint[policy.LightCapacity*32],0);frame=0;historySlots.Clear();}
    }

    /// <summary>Ends owned GPU storage through the existing RHI retirement path.</summary>
    public void Dispose()
    {
        if(disposed)return;disposed=true;frame=0;historySlots.Clear();
        // Terminal context teardown cannot issue uploads into a retiring namespace.
        shader.Dispose();inputs.Dispose();Results.Dispose();history.Dispose();Priority.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Creates one bounded mutable SSBO through RHI.</summary>
    private static GpuShaderStorageBuffer Buffer(int bytes,string name)=>RHICommandContext.Current.CreateShaderStorageBuffer(
        new RHIBufferDescription{Role=BufferRole.ShaderStorage,Mode=ResourceStorageMode.Mutable,Capacity=bytes,Usage=BufferUsage.DynamicDraw,Label="Shadows.Readiness."+name}).RequireResource();
    #endregion
}
