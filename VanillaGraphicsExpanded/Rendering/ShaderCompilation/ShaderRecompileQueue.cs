using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Coalesces configuration edits across programs into one render-thread linking batch.</summary>
internal sealed class ShaderRecompileQueue
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, ShaderRecompileQueue> queues = new();
    private readonly object gate = new();
    private readonly HashSet<GpuProgram> pending = new();
    private bool scheduled;

    #region Scheduling
    /// <summary>Records an owner once and queues a single callback for the API's changed programs.</summary>
    internal static void Enqueue(ICoreClientAPI api, GpuProgram program)
    {
        var queue = queues.GetValue(api, _ => new ShaderRecompileQueue());
        lock (queue.gate)
        {
            queue.pending.Add(program);
            if (queue.scheduled) return;
            queue.scheduled = true;
        }
        api.Event.EnqueueMainThreadTask(() => queue.Drain(api), "vge:recompile");
    }

    /// <summary>Captures final settings after coalescing, leaving edits raised during publication for another callback.</summary>
    private void Drain(ICoreClientAPI api)
    {
        GpuProgram[] programs;
        lock (gate)
        {
            programs = pending.ToArray();
            pending.Clear();
            scheduled = false;
        }
        foreach (var group in programs.Where(program => program.NeedsRecompile).GroupBy(program =>
            string.IsNullOrWhiteSpace(program.AssetDomain) ? ShaderImportsSystem.DefaultDomain : program.AssetDomain))
        {
            try
            {
                using var batch = new ShaderLinkBatch(api.Assets, group.Key, group.Select(program => program.RequestedSettings));
                foreach (var program in group)
                    if (program.NeedsRecompile) program.CompileAndLink();
            }
            catch (Exception error) { api.Logger.Error("[VGE] Shader recompile batch failed: {0}", error); }
        }
    }
    #endregion
}
