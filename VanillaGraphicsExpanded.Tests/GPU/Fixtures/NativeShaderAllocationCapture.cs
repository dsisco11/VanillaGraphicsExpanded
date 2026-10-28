using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes native candidate allocations without exposing provisional identities through semantic shader requests.</summary>
internal sealed unsafe class NativeShaderAllocationCapture : IDisposable
{
    [ThreadStatic] private static NativeShaderAllocationCapture? current;
    private readonly Harmony harmony = new($"VGE.Tests.NativeShaderAllocations.{Guid.NewGuid()}");
    private readonly NativeDriverCapture native = new();
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
            nint link = native.Original("glLinkProgram");
            native.Add("glLinkProgram", new Link(program =>
            {
                native.Observe(() => RecordAssociation(unchecked((int)program)));
                ((delegate* unmanaged[Stdcall]<uint, void>)link)(program);
            }));
            native.Install();
        }
        catch { Dispose(); throw; }
    }
    #endregion
    #region Public API
    /// <summary>Stops native observation without changing candidate or result ownership.</summary>
    public void Dispose() { native.Dispose(); harmony.UnpatchAll(harmony.Id); if (ReferenceEquals(current, this)) current = null; }
    #endregion
    #region Private
    /// <summary>Preserves the native program-link ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Link(uint program);
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
