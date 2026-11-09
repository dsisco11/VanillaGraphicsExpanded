using Microsoft.CodeAnalysis;
using OpenGLInterceptionGenerator.Tests.Support.Generation;

namespace OpenGLInterceptionGenerator.Tests.Component.Generation;

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
        // Arrange: add the candidate observer declaration to a minimal generated type.
        var source = "internal static partial class OpenGLBindingInterception { " + method + " }";
        const string nativeDeclarations = "glDepthMask|void|byte";
        // Act: run the generator against the known native signature.
        var result = GeneratorCompilation.Run(source, nativeDeclarations);
        // Assert: invalid observer declarations produce the named generator diagnostic.
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.GetMessage().Contains(expected));
    }

    /// <summary>Rejects unsupported native ABI types and ambiguous binding-name layouts.</summary>
    [Theory]
    [InlineData("glDepthMask|void|object")]
    [InlineData("glDepthMask|void|byte\nglDepthMask|void|byte")]
    public void InvalidBindingLayoutsAreBuildErrors(string inventory)
    {
        // Arrange: use a valid observer and the supplied invalid native inventory.
        const string source = "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }";
        // Act: run the generator over that inventory.
        var result = GeneratorCompilation.Run(source, inventory);
        // Assert: the unsupported or ambiguous inventory is rejected.
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    /// <summary>Detects conflicting call-site ABIs between the two referenced binding tables.</summary>
    [Fact]
    public void ConflictingBindingFamiliesAreBuildErrors()
    {
        // Arrange: provide conflicting ABI signatures for the same native name in both binding families.
        const string source = "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }";
        const string nativeDeclarations = "glDepthMask|void|byte";
        const string coreDeclarations = "glDepthMask|void|int";
        // Act: run the generator with both tables.
        var result = GeneratorCompilation.Run(source, nativeDeclarations, coreDeclarations);
        // Assert: conflicting ABI families are reported as an error.
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.GetMessage().Contains("Conflicting OpenTK native signatures"));
    }

    /// <summary>Public convenience parameters do not replace native count-and-pointer arguments.</summary>
    [Fact]
    public void ConvenienceOverloadsUseTheNativeCallSiteSignature()
    {
        // Arrange: observe the native count-and-pointer parameters of glDeleteBuffers.
        const string source = "internal static unsafe partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDeleteBuffers\")] static void Observe(int count, uint* values) {} }";
        const string declarations = "glDeleteBuffers|void|int,uint*";
        // Act: run the generator and compiler against that binding declaration.
        var result = GeneratorCompilation.Run(source, declarations);
        // Assert: the native signature is accepted by both generator and compiler.
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>Native symbols are resolved by name when binding families use different slot ordering.</summary>
    [Fact]
    public void DifferentSlotOrdersAndUnselectedMethodsAreSupported()
    {
        // Arrange: provide equivalent selected declarations at different table positions.
        const string source = "internal static partial class OpenGLBindingInterception { [InterceptOpenGL(\"glDepthMask\")] static void Observe(byte value) {} }";
        const string nativeDeclarations = "glDepthMask|void|byte\nglUnused|void|object";
        const string coreDeclarations = "glUnused|void|object\nglDepthMask|void|byte";
        // Act: run generation and compilation with the extra unselected method.
        var result = GeneratorCompilation.Run(source, nativeDeclarations, coreDeclarations);
        // Assert: lookup by native name tolerates ordering and leaves unselected declarations harmless.
        Assert.Empty(result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
    /// <summary>Equivalent native symbols can call one accessible method on the cache authority.</summary>
    [Fact]
    public void AliasesShareCacheObservation()
    {
        // Arrange: bind two native aliases to the same observer method.
        const string source = """
            internal sealed class Cache
            {
                [InterceptOpenGL("glProbe")]
                [InterceptOpenGL("glProbeARB")]
                internal static void Observe(int value) { }
            }
            """;
        const string declarations = "glProbe|void|int\nglProbeARB|void|int";
        // Act: generate source for both declarations.
        var result = GeneratorCompilation.Run(source, declarations);
        // Assert: both aliases generate calls to the shared observer.
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", result.Compilation.SyntaxTrees.Select(tree => tree.ToString()));
        Assert.Contains("Cache.@Observe(a0)", generated);
        Assert.Contains("CreateProbeARB", generated);
    }

    #endregion
}
