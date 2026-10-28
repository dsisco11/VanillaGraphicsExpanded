using System.Reflection;
using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator.Tests;

/// <summary>Executes generated unmanaged callbacks to verify forwarding order, outputs and failure containment.</summary>
public sealed class NativeForwardingTests
{
    #region Public API
    /// <summary>Preserves arguments, native output and return values when the post-call observer throws.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NativeResultSurvivesObserverFailure(bool fail, bool disableObservation)
    {
        var source = """
            internal static unsafe partial class OpenGLBindingInterception
            {
                private static int calls, observed;
                private static bool fail;
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static uint Driver(uint input, int* output)
                {
                    calls++;
                    *output = (int)input + 2;
                    return input + 3;
                }
                [InterceptOpenGL("glProbe")]
                private static void Observe(uint input, int* output, uint result)
                {
                    if (calls != 1 || *output != (int)input + 2 || result != input + 3)
                        throw new Exception("Observation ran before native output existed.");
                    observed++;
                    if (fail) throw new Exception("Injected observer fault.");
                }
                public static int[] Run(bool shouldFail, bool disableObservation)
                {
                    fail = shouldFail;
                    ObservationEnabled = !disableObservation;
                    var slot = CreateProbe((nint)(delegate* unmanaged[Stdcall]<uint, int*, uint>)&Driver);
                    var address = Marshal.GetFunctionPointerForDelegate(slot.Callback);
                    int output = 0;
                    uint result = ((delegate* unmanaged[Stdcall]<uint, int*, uint>)address)(41, &output);
                    GC.KeepAlive(slot);
                    return new[] { calls, observed, output, (int)result };
                }
            }
            """;
        var generated = GeneratorCompilation.Run(source, "glProbe|uint|uint,int*");
        Assert.Empty(generated.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = generated.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var method = assembly.GetType("VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBindingInterception")!.GetMethod("Run")!;
        Assert.Equal(new[] { 1, disableObservation ? 0 : 1, 43, 44 }, (int[])method.Invoke(null, new object[] { fail, disableObservation })!);
    }
    #endregion
}
