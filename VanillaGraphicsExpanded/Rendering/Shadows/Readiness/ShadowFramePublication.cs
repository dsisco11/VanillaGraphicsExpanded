using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>One current-light interval's GPU eligibility and selected rendered mappings, borrowed until surface submission ends.</summary>
internal sealed class ShadowFramePublication
{
    private bool active=true;
    internal long Frame { get; }
    internal ShadowLightSnapshot Lights { get; }
    internal ShadowPagePool Pages { get; }
    internal GpuShaderStorageBuffer Requests { get; }
    internal GpuShaderStorageBuffer Readiness { get; }
    internal GpuShaderStorageBuffer? SampleFailures { get; }
    internal ImmutableDictionary<ShadowSourceKey,int> SourceIndices { get; }
    internal uint LightCount=>(uint)SourceIndices.Count;
    /// <summary>Rejects ended, superseded, disabled, reconfigured and capability-withdrawn intervals before binding.</summary>
    internal bool IsCurrent=>active&&Pages.CanSubmit;

    #region Public API
    /// <summary>Borrows the exact owning frame resources without taking their allocation or retirement authority.</summary>
    internal ShadowFramePublication(ShadowLightSnapshot lights,ShadowPagePool pages,
        GpuShaderStorageBuffer requests,GpuShaderStorageBuffer readiness,GpuShaderStorageBuffer? sampleFailures=null,
        ImmutableDictionary<ShadowSourceKey,int>? sourceIndices=null)
    {
        SourceIndices=sourceIndices??ImmutableDictionary<ShadowSourceKey,int>.Empty;
        int count=SourceIndices.Count;
        if(count>102 || (count>0 && (readiness.SizeBytes<count*128 || sampleFailures is null || sampleFailures.SizeBytes<count*16)))
            throw new ArgumentException("Surface source indices exceed their bounded GPU result storage.",nameof(sourceIndices));
        var known=new HashSet<ShadowSourceKey>{lights.Sun.Source,lights.Moon.Source};
        foreach(var point in lights.Points)known.Add(point.Source);
        var used=new bool[count];
        foreach(var pair in SourceIndices)
        {
            if(!pair.Key.IsKnown || !known.Contains(pair.Key) || pair.Value<0 || pair.Value>=count || used[pair.Value])
                throw new ArgumentException("Surface source indices must uniquely identify the exact current-light result rows.",nameof(sourceIndices));
            used[pair.Value]=true;
        }
        Frame=lights.Frame;Lights=lights;Pages=pages;Requests=requests;Readiness=readiness;SampleFailures=sampleFailures;
    }
    /// <summary>Withdraws this interval permanently so reopening the same pool cannot revive it.</summary>
    internal void Withdraw(){active=false;}
    #endregion
}
