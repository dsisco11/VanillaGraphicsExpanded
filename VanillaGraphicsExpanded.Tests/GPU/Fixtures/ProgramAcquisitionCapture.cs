using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes program acquisition and native interface reads at their actual boundaries.</summary>
internal sealed class ProgramAcquisitionCapture : IDisposable
{
    private readonly Harmony harmony = new("Tests.ProgramAcquisition." + Guid.NewGuid());
    private static ProgramAcquisitionCapture? active;
    /// <summary>Counts all observed acquisition and native interface reads.</summary>
    internal int Reads { get; private set; }
    /// <summary>Counts complete canonical program acquisitions separately from narrow output reads.</summary>
    internal int Acquisitions { get; private set; }

    #region Public API
    /// <summary>Installs test-only observations without altering query behavior.</summary>
    internal ProgramAcquisitionCapture()
    {
        Assert.Null(active);
        active = this;
        var prefix = new HarmonyMethod(typeof(ProgramAcquisitionCapture), nameof(Count));
        harmony.Patch(typeof(GpuProgramMetadataReader).GetMethod(nameof(GpuProgramMetadataReader.TryRead))!, prefix: prefix);
        foreach (var method in typeof(GL).GetMethods(BindingFlags.Public | BindingFlags.Static))
            if (method.Name is "GetProgramInterface" or "GetProgramResource" or "GetUniform" or "GetActiveAtomicCounterBuffer")
                harmony.Patch(method, prefix: prefix);
    }

    /// <summary>Verifies native interception before measuring preparation.</summary>
    internal void Calibrate(int program)
    {
        int before = Reads;
        GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
        Assert.True(count >= 0);
        Assert.True(Reads > before);
        Reset();
    }

    /// <summary>Starts an interval after acquisition or other intentional driver reads.</summary>
    internal void Reset() { Reads = 0; Acquisitions = 0; }

    /// <summary>Removes only these observations.</summary>
    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion

    #region Private
    /// <summary>Counts acquisition before executing its original implementation.</summary>
    private static void Count(MethodBase __originalMethod)
    {
        if (active is not { } capture) return;
        capture.Reads++;
        // Full descriptor acquisition is distinct from a required narrow native result read.
        if (__originalMethod.DeclaringType == typeof(GpuProgramMetadataReader)) capture.Acquisitions++;
    }
    #endregion
}
