using Microsoft.CodeAnalysis;

namespace OpenGLInterceptionGenerator.Tests;

/// <summary>Checks compilation-time rejection of declarations that cannot safely observe the selected ABI.</summary>
public sealed class ObserverContractTests
{
    #region Public API
    /// <summary>Rejects misspelled native symbols, incompatible widths and duplicate registrations.</summary>
    [Theory]
    [InlineData("[InterceptOpenGL(\"glUnknown\")] static void Observe(byte a) {}", "Unknown native entrypoint")]
    [InlineData("[InterceptOpenGL(\"glDepthMask\")] static void Observe(bool a) {}", "Observer signature")]
    [InlineData("[InterceptOpenGL(\"glDepthMask\")] static int Observe(byte a) => 0;", "Observers must")]
    [InlineData("[InterceptOpenGL(\"glDepthMask\")] static void Observe(ref byte a) {}", "Observer signature")]
    [InlineData("[InterceptOpenGL(\"glDepthMask\")] static void Observe<T>(byte a) {}", "Observers must")]
    [InlineData("[InterceptOpenGL(\"glDepthMask\")] static void First(byte a) {} [InterceptOpenGL(\"glDepthMask\")] static void Second(byte a) {}", "Duplicate native observer")]
    public void InvalidDeclarationsAreBuildErrors(string method, string expected)
    {
        var result = GeneratorCompilation.Run("internal static partial class OpenGLBindingInterception { " + method + " }", "glDepthMask|void|byte");
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains(expected));
    }

    /// <summary>Rejects unsupported native ABI types and ambiguous binding-name layouts.</summary>
    [Theory]
    [InlineData("glDepthMask|void|object")]
    [InlineData("glDepthMask|void|byte\nglDepthMask|void|byte")]
    public void InvalidBindingLayoutsAreBuildErrors(string inventory)
    {
        var result = GeneratorCompilation.Run("internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }", inventory);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Detects conflicting call-site ABIs between the two referenced binding tables.</summary>
    [Fact]
    public void ConflictingBindingFamiliesAreBuildErrors()
    {
        var result = GeneratorCompilation.Run(
            "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }",
            "glDepthMask|void|byte", "glDepthMask|void|int");
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.GetMessage().Contains("Conflicting OpenTK native signatures"));
    }

    /// <summary>Public convenience parameters do not replace native count-and-pointer arguments.</summary>
    [Fact]
    public void ConvenienceOverloadsUseTheNativeCallSiteSignature()
    {
        var result = GeneratorCompilation.Run(
            "internal static unsafe partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDeleteBuffers\")] static void Observe(int count, uint* values) {} }",
            "glDeleteBuffers|void|int,uint*");
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>Native symbols are resolved by name when binding families use different slot ordering.</summary>
    [Fact]
    public void DifferentSlotOrdersAndUnselectedMethodsAreSupported()
    {
        var result = GeneratorCompilation.Run(
            "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }",
            "glDepthMask|void|byte\nglUnused|void|object", "glUnused|void|object\nglDepthMask|void|byte");
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
    /// <summary>Equivalent native symbols can call one accessible method on the cache authority.</summary>
    [Fact]
    public void AliasesShareCacheObservation()
    {
        var result = GeneratorCompilation.Run("""
            internal sealed class Cache
            {
                [InterceptOpenGL("glProbe")]
                [InterceptOpenGL("glProbeARB")]
                internal static void Observe(int value) { }
            }
            """, "glProbe|void|int\nglProbeARB|void|int");
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", result.Compilation.SyntaxTrees.Select(tree => tree.ToString()));
        Assert.Contains("Cache.@Observe(a0)", generated);
        Assert.Contains("CreateProbeARB", generated);
    }

    #endregion
}
