using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes native candidate allocations without exposing provisional identities through semantic shader requests.</summary>
internal sealed class NativeShaderAllocationCapture : IDisposable
{
    [ThreadStatic] private static NativeShaderAllocationCapture? current;
    private readonly Harmony harmony = new($"VGE.Tests.NativeShaderAllocations.{Guid.NewGuid()}");
    private readonly List<int> programs = new(), stages = new();
    #region Internal API
    /// <summary>Gets the sole candidate program allocated inside the observed creation interval.</summary>
    internal int Program => programs.Single();
    /// <summary>Gets the stage names whose eventual deletion must be established independently.</summary>
    internal int[] Stages => stages.ToArray();
    /// <summary>Gets the manager association observed when the original executable was submitted.</summary>
    internal GpuResourceRegistration? Registration { get; private set; }
    /// <summary>Starts observation of exact core allocations and link submission on this test thread.</summary>
    internal NativeShaderAllocationCapture()
    {
        if (current is not null) throw new InvalidOperationException("Native allocation capture cannot nest.");
        current = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.CreateProgram), Type.EmptyTypes),
                postfix: new HarmonyMethod(typeof(NativeShaderAllocationCapture), nameof(RecordProgram)));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.CreateShader), [typeof(ShaderType)]),
                postfix: new HarmonyMethod(typeof(NativeShaderAllocationCapture), nameof(RecordStage)));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.LinkProgram), [typeof(int)]),
                prefix: new HarmonyMethod(typeof(NativeShaderAllocationCapture), nameof(RecordAssociation)));
        }
        catch { Dispose(); throw; }
    }
    #endregion
    #region Public API
    /// <summary>Stops native observation without changing candidate or result ownership.</summary>
    public void Dispose() { harmony.UnpatchAll(harmony.Id); if (ReferenceEquals(current, this)) current = null; }
    #endregion
    #region Private
    /// <summary>Captures the returned program name without altering native execution.</summary>
    private static void RecordProgram(int __result) => current?.programs.Add(__result);
    /// <summary>Captures temporary stages before their native ownership transfers or retires.</summary>
    private static void RecordStage(int __result) => current?.stages.Add(__result);
    /// <summary>Captures the published original manager entry before deferred executable submission.</summary>
    private static void RecordAssociation(int __0)
    {
        if (current is { } capture)
            capture.Registration = GpuResourceManagerSystem.CurrentManager?.FindResource(GpuResourceKind.Program, __0);
    }
    #endregion
}
