using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes canonical program acquisition and actual native interface outputs.</summary>
internal sealed unsafe class ProgramAcquisitionCapture : IDisposable
{
    private readonly Harmony harmony = new("Tests.ProgramAcquisition." + Guid.NewGuid());
    private readonly NativeDriverCapture native = new();
    [ThreadStatic] private static ProgramAcquisitionCapture? active;

    /// <summary>Matches program interface output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void InterfaceOutput(uint program, int kind, int parameter, int* value);
    /// <summary>Matches full program resource output, including its native property/result buffers.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ResourceOutput(uint program, int kind, uint index, int count, int* properties, int size, int* length, int* values);
    /// <summary>Matches scalar/array uniform output whose pointed-to scalar width is left unchanged.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UniformOutput(uint program, int location, nint values);
    /// <summary>Matches atomic counter buffer output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void AtomicOutput(uint program, uint index, int parameter, int* values);

    #region Internal API
    /// <summary>Counts acquisition and native interface/result reads.</summary>
    internal int Reads { get; private set; }
    /// <summary>Counts complete canonical acquisitions separately from driver outputs.</summary>
    internal int Acquisitions { get; private set; }

    /// <summary>Installs one managed acquisition observer and exact native result decorators.</summary>
    internal ProgramAcquisitionCapture()
    {
        Assert.Null(active);
        active = this;
        try
        {
            harmony.Patch(typeof(GpuProgramMetadataReader).GetMethod(nameof(GpuProgramMetadataReader.TryRead))!,
                prefix: new HarmonyMethod(typeof(ProgramAcquisitionCapture), nameof(CountAcquisition)));
            var getInterface = (delegate* unmanaged[Stdcall]<uint, int, int, int*, void>)native.Original("glGetProgramInterfaceiv");
            native.Add("glGetProgramInterfaceiv", new InterfaceOutput((program, kind, parameter, value) =>
            {
                getInterface(program, kind, parameter, value); Reads++;
            }));
            var getResource = (delegate* unmanaged[Stdcall]<uint, int, uint, int, int*, int, int*, int*, void>)native.Original("glGetProgramResourceiv");
            native.Add("glGetProgramResourceiv", new ResourceOutput((program, kind, index, count, properties, size, length, values) =>
            {
                getResource(program, kind, index, count, properties, size, length, values); Reads++;
            }));
            foreach (string symbol in new[] { "glGetUniformiv", "glGetUniformuiv", "glGetUniformfv", "glGetUniformdv" })
            {
                var original = (delegate* unmanaged[Stdcall]<uint, int, nint, void>)native.Original(symbol);
                native.Add(symbol, new UniformOutput((program, location, values) => { original(program, location, values); Reads++; }));
            }
            var atomic = (delegate* unmanaged[Stdcall]<uint, uint, int, int*, void>)native.Original("glGetActiveAtomicCounterBufferiv");
            native.Add("glGetActiveAtomicCounterBufferiv", new AtomicOutput((program, index, parameter, values) =>
            {
                atomic(program, index, parameter, values); Reads++;
            }));
            native.Install();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Verifies both binding roots before measuring preparation.</summary>
    internal void Calibrate(int program)
    {
        int before = Reads;
        GL.GetProgramInterface(program, ProgramInterface.Uniform, ProgramInterfaceParameter.ActiveResources, out int count);
        Assert.True(count >= 0);
        Assert.Equal(before + 1, Reads);
        OpenTK.Graphics.OpenGL4.GL.GetProgramInterface(program, OpenTK.Graphics.OpenGL4.ProgramInterface.Uniform,
            OpenTK.Graphics.OpenGL4.ProgramInterfaceParameter.ActiveResources, out int nativeCount);
        Assert.True(nativeCount >= 0);
        Assert.Equal(before + 2, Reads);
        Reset();
    }

    /// <summary>Excludes setup and intentional output reads from the next interval.</summary>
    internal void Reset() { Reads = 0; Acquisitions = 0; }
    #endregion

    #region Public API
    /// <summary>Restores native dispatch and removes only this fixture's acquisition observer.</summary>
    public void Dispose()
    {
        native.Dispose();
        harmony.UnpatchAll(harmony.Id);
        if (ReferenceEquals(active, this)) active = null;
    }
    #endregion

    #region Private
    /// <summary>Counts a canonical acquisition independently of the native outputs it may request.</summary>
    private static void CountAcquisition()
    {
        if (active is not { } capture) return;
        capture.Reads++;
        capture.Acquisitions++;
    }
    #endregion
}
