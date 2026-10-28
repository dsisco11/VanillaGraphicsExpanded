using System.Reflection;
using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator.Tests;

/// <summary>Exercises generated original-pointer calls, including distinct native slots for managed overloads.</summary>
public sealed class DriverForwardingTests
{
    #region Public API
    /// <summary>Resolves overloaded methods independently and bypasses installed observation callbacks.</summary>
    [Fact]
    public void ExactOverloadsReachTheirOriginalDriver()
    {
        var result = GeneratorCompilation.Run("""
            internal static partial class OpenGLBindingInterception
            {
                internal static nint Original(nint pointer) => pointer == 1 ? DriverTest.First : pointer == 2 ? DriverTest.Second : DriverTest.Third;
            }
            internal sealed class DriverTable
            {
                internal nint Read(string name) => name == "glProbe" ? 1 : name == "glProbeIndexed" ? 2 : 3;
            }
            internal static unsafe partial class OpenGLDriverCalls
            {
                private static readonly DriverTable Core = new();
                private static readonly DriverTable Legacy = Core;
            }
            public static unsafe class DriverTest
            {
                internal static nint First => (nint)(delegate* unmanaged[Stdcall]<int, void>)&One;
                internal static nint Second => (nint)(delegate* unmanaged[Stdcall]<int, int, void>)&Two;
                internal static nint Third => (nint)(delegate* unmanaged[Stdcall]<uint, void>)&Unsigned;
                private static int value;
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static void One(int input) { value = input; }
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static void Two(int first, int second) { value += first * second; }
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static void Unsigned(uint input) { value += (int)input; }
                public static int Run()
                {
                    OpenGLDriverCalls.Probe(3);
                    OpenGLDriverCalls.Probe(4, 5);
                    OpenGLDriverCalls.Probe(7u);
                    return value;
                }
            }
            """, "glProbe|void|int|Probe\nglProbeIndexed|void|int,int|Probe\nglProbeUnsigned|void|uint|Probe");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(30, assembly.GetType("VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.DriverTest")!
            .GetMethod("Run")!.Invoke(null, null));
    }
    /// <summary>A scalar deletion overload supplies one stable native element rather than a handle-shaped pointer.</summary>
    [Fact]
    public void ScalarConvenienceOverloadPreservesCountAndAddress()
    {
        var result = GeneratorCompilation.Run("""
            internal static partial class OpenGLBindingInterception
            {
                internal static nint Original(nint pointer) => pointer;
            }
            internal sealed class DriverTable
            {
                internal nint Read(string name) => DriverTest.Pointer;
            }
            internal static unsafe partial class OpenGLDriverCalls
            {
                private static readonly DriverTable Core = new();
                private static readonly DriverTable Legacy = Core;
            }
            public static unsafe class DriverTest
            {
                internal static nint Pointer => (nint)(delegate* unmanaged[Stdcall]<int, uint*, void>)&Delete;
                private static uint value;
                private static int count;
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static void Delete(int n, uint* ids) { count = n; value = *ids; }
                public static uint[] Run()
                {
                    OpenGLDriverCalls.DeleteOne(uint.MaxValue);
                    return new[] { (uint)count, value };
                }
            }
            """, "glDeleteBuffers|void|int,uint*");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        Assert.Equal(new[] { 1u, uint.MaxValue }, (uint[])assembly.GetType("VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.DriverTest")!
            .GetMethod("Run")!.Invoke(null, null)!);
    }
    #endregion
}
