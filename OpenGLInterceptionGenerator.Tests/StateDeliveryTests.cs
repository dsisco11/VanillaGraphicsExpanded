using System.Reflection;
using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator.Tests;

/// <summary>Exercises generated scalar delivery independently of the real driver and cache implementation.</summary>
public sealed class StateDeliveryTests
{
    private const string Registry = """
        namespace VanillaGraphicsExpanded.Rendering.Integration;
        internal static class RenderContextRegistry
        {
            internal static (nint Handle, long Generation) CaptureResourceContext() => (1, 1);
            internal static bool IsCurrentResourceContext((nint Handle, long Generation) origin) => origin == (1, 1);
        }
        """;
    private const string Cache = """
        internal sealed class OpenGlStateCache
        {
            internal static OpenGlStateCache Current = new();
            internal int Withdrawn, Observed;
            internal bool Live = true;
            internal (nint Handle, long Generation) KnowledgeContext => (1, 1);
            internal bool OwnsKnowledgeFor((nint Handle, long Generation) origin) => Live;
            internal int LastWithdrawn = -1;
        }
        internal static class OpenGLObservationContext
        {
            internal static bool TryGet(out OpenGlStateCache cache, out (nint Handle, long Generation) origin, bool requireCapabilities)
            { cache = OpenGlStateCache.Current; origin = (1, 1); return true; }
        }
        """;

    #region Public API
    /// <summary>Known rejection, uncertainty, delivery failure and replacement withdraw only their declared field.</summary>
    [Theory]
    [InlineData(0, false, false, 0, 0)]
    [InlineData(1, false, false, 1, 1)]
    [InlineData(2, false, false, 1, 0)]
    [InlineData(1, true, false, 2, 1)]
    [InlineData(1, true, true, 1, 1)]
    [InlineData(3, false, false, 0, 0)]
    [InlineData(4, false, false, 2, 0)]
    public void ClassifiedDeliveryPreservesNativeDispatch(int outcome, bool fail, bool replace, int withdrawals, int observations)
    {
        var source = Cache + """
            internal static unsafe partial class OpenGLBindingInterception
            {
                private static int calls;
                private static bool fail, replace;
                [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvStdcall) })]
                private static void Driver(byte value) { calls++; }
                [InterceptOpenGL("glDepthMask", Withdraw = nameof(Withdraw), Classify = nameof(Classify))]
                internal static void Observe(OpenGlStateCache cache, byte value)
                {
                    cache.Observed++;
                    if (replace) cache.Live = false;
                    if (fail) throw new Exception("Injected delivery fault.");
                }
                /// <summary>Uses native arguments to choose the affected knowledge in the cache domain.</summary>
                internal static void Withdraw(OpenGlStateCache cache, byte value)
                {
                    cache.Withdrawn++;
                    cache.LastWithdrawn = value;
                    if (value == 4) throw new Exception("Injected withdrawal fault.");
                }
                internal static OpenGLObservedEffect Classify(byte value)
                {
                    if (value == 3) { OpenGlStateCache.Current.Live = false; return OpenGLObservedEffect.Changed; }
                    return value == 4 ? OpenGLObservedEffect.Uncertain : (OpenGLObservedEffect)value;
                }
                public static int[] Run(byte outcome, bool shouldFail, bool shouldReplace)
                {
                    fail = shouldFail; replace = shouldReplace;
                    var slot = CreateDepthMask((nint)(delegate* unmanaged[Stdcall]<byte, void>)&Driver);
                    var pointer = Marshal.GetFunctionPointerForDelegate(slot.Callback);
                    ((delegate* unmanaged[Stdcall]<byte, void>)pointer)(outcome);
                    GC.KeepAlive(slot);
                    return new[] { calls, OpenGlStateCache.Current.Withdrawn, OpenGlStateCache.Current.Observed, OpenGlStateCache.Current.LastWithdrawn };
                }
            }
            """;
        var result = GeneratorCompilation.Run(source, "glDepthMask|void|byte", supportSource: Registry);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = result.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var method = Assembly.Load(stream.ToArray()).GetType("VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBindingInterception")!.GetMethod("Run")!;
        Assert.Equal(new[] { 1, withdrawals, observations, withdrawals == 0 ? -1 : outcome }, (int[])method.Invoke(null, new object[] { (byte)outcome, fail, replace })!);
    }

    /// <summary>Missing or incompatible withdrawal/classifier methods are compile-time contract errors.</summary>
    [Theory]
    [InlineData("", "", "Withdrawal")]
    [InlineData("Withdraw = \"Missing\"", "", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "internal static int Withdraw(OpenGlStateCache cache, byte value) => 0;", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "internal static void Withdraw(OpenGlStateCache cache, int value) {}", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "internal static void Withdraw(byte value) {}", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "internal static void Withdraw(OpenGlStateCache cache, ref byte value) {}", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "private static void Withdraw(OpenGlStateCache cache, byte value) {}", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw)", "internal static void Withdraw<T>(OpenGlStateCache cache, byte value) {}", "Withdrawal")]
    [InlineData("Withdraw = nameof(Withdraw), Classify = nameof(Classify)", "internal static void Withdraw(OpenGlStateCache cache, byte value) {} internal static bool Classify(byte value) => true;", "Classifier")]
    public void InvalidStateDeclarationsAreBuildErrors(string declaration, string definitions, string expected)
    {
        var source = Cache + "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\""
            + (declaration.Length == 0 ? "" : ", " + declaration)
            + ")] internal static void Observe(OpenGlStateCache cache, byte value) {} " + definitions + " }";
        var result = GeneratorCompilation.Run(source, "glDepthMask|void|byte", supportSource: Registry);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains(expected, StringComparison.OrdinalIgnoreCase));
    }
    #endregion
}
