using System.Collections.Immutable;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;


namespace OpenGLInterceptionGenerator.Tests;

/// <summary>Compiles real generated callbacks against a minimal native lifetime boundary.</summary>
internal static class GeneratorCompilation
{
    private static readonly ConcurrentDictionary<string, string> bindings = new();

    internal const string Prelude = """
        using System;
        using System.Runtime.InteropServices;
        namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
        internal static partial class OpenGLBindingInterception { internal static bool ObservationEnabled = true; }
        [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
        internal sealed class InterceptOpenGLAttribute(string name) : Attribute
        {
            public string? Classify { get; set; }
            public string? Withdraw { get; set; }
        }
        internal enum OpenGLObservedEffect { Rejected, Changed, Uncertain }
        internal sealed class OpenGLNativeSlot(nint original, Delegate callback)
        {
            internal readonly Delegate Callback = callback;
        }
        """;

    #region Internal API
    /// <summary>Runs the incremental generator and retains compiler diagnostics for ABI contract tests.</summary>
    internal static (Compilation Compilation, ImmutableArray<Diagnostic> Diagnostics) Run(string source, string nativeDeclarations, string? coreDeclarations = null, string? supportSource = null)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(bindings.GetOrAdd(nativeDeclarations + "\n#CORE#\n" + (coreDeclarations ?? nativeDeclarations), EmitBindings)));
        var options = new CSharpParseOptions(LanguageVersion.CSharp13);
        var compilation = CSharpCompilation.Create("GeneratedHooks_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(Prelude + source, options), CSharpSyntaxTree.ParseText(supportSource ?? "", options) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new InterceptionGenerator().AsSourceGenerator() }, parseOptions: options);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var generated, out var diagnostics);
        return (generated, diagnostics);
    }
    #endregion

    #region Private
    /// <summary>Builds a real PE fixture with native calli and name-table IL, independent of observer declarations.</summary>
    private static string EmitBindings(string declarations)
    {
        var roots = declarations.Split(new[] { "\n#CORE#\n" }, StringSplitOptions.None);
        var tree = CSharpSyntaxTree.ParseText("namespace OpenTK.Graphics.OpenGL { " + BindingRoot(roots[0])
            + " } namespace OpenTK.Graphics.OpenGL4 { " + BindingRoot(roots[1]) + " }");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("OpenTK.Graphics", new[] { tree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var directory = Path.Combine(Path.GetTempPath(), "VGE-OpenTK-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "OpenTK.Graphics.dll");
        var emitted = compilation.Emit(file);
        if (!emitted.Success) throw new InvalidOperationException(string.Join("\n", emitted.Diagnostics));
        return file;
    }

    /// <summary>Builds supported table initialization and native calls, including a managed convenience overload.</summary>
    private static string BindingRoot(string declarations)
    {
        var rows = declarations.Split('\n').Select(line => line.Split('|')).ToArray();
        var names = string.Join(",", rows.Select(row => "\"" + row[0] + "\""));
        var methods = rows.Select((row, index) =>
        {
            var types = row[2].Length == 0 ? Array.Empty<string>() : row[2].Split(',');
            string parameters = string.Join(",", types.Select((type, i) => type + " a" + i));
            string arguments = string.Join(",", types.Select((_, i) => "a" + i));
            string signature = string.Join(",", types.Concat(new[] { row[1] }));
            return $"public static {row[1]} {(row.Length > 3 ? row[3] : "Call" + index)}({parameters}) {{ {(row[1] == "void" ? "" : "return ")}((delegate* unmanaged[Stdcall]<{signature}>)EntryPoints[{index}])({arguments}); }}";
        });
        string convenience = rows[0][0] == "glDeleteBuffers"
            ? "public static void DeleteOne(uint id) { ((delegate* unmanaged[Stdcall]<int,uint*,void>)EntryPoints[0])(1, &id); }" : "";
        return $"public static unsafe class GL {{ private static readonly nint[] EntryPoints = new nint[{rows.Length}]; private static readonly string[] EntryPointNames = new[] {{ {names} }}; {string.Join("\n", methods)} {convenience} }}";
    }
    #endregion
}
