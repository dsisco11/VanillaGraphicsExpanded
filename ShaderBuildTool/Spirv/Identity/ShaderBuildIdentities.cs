using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Separates processing implementation, compiler and catalogue identities for independently reusable results.</summary>
internal sealed record ShaderBuildIdentities(string Preprocessing, string Emission, string Compiler, string Interface)
{
    #region Public API
    /// <summary>Hashes the implementation dependency closure while excluding declaration data from every processing key.</summary>
    internal static ShaderBuildIdentities Capture(string workingDirectory, string target, bool warnings,
        ShaderFileHashIndex? index = null, Dictionary<string, string>? details = null)
    {
        string[] managed = ImplementationFiles().ToArray();
        string implementation = Files("processing-implementation-v1", managed, index, details);
        // Native reflection is shipped alongside the tool, and must participate even before it is loaded.
        string runtimeRoot = Path.Combine(AppContext.BaseDirectory, "runtimes");
        var native = Directory.Exists(runtimeRoot) ? Directory.EnumerateFiles(runtimeRoot, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).Contains("spirv-cross", StringComparison.OrdinalIgnoreCase)) : [];
        string reflection = Files("reflection-native-v1", native, index, details);
        return new(Hash("preprocessing-v1", implementation), Hash("emission-v1", implementation),
            ShaderBuildReceipt.CompilerFingerprint(workingDirectory, target, warnings, index, details),
            Hash("interface-v1", implementation, reflection, ShaderInterfaceExtraction.ExtractorIdentity,
                PackagedShaderInterface.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>Includes processing changes and resolved catalogue membership in the whole-build shortcut only.</summary>
    internal string Receipt(ShaderVariantResolver registry, string scope, Dictionary<string, string>? details = null)
    {
        string membership = Convert.ToHexString(SHA256.HashData(ShaderContractProjection.Membership(registry, scope)));
        if (details != null) details["catalogue membership"] = membership;
        return Hash("build-inputs-v2", Preprocessing, Emission, Compiler, Interface, membership);
    }

    /// <summary>Enumerates managed processing dependencies without catalogue data or shared framework binaries.</summary>
    internal static IEnumerable<string> ImplementationFiles()
    {
        var pending = new Stack<Assembly>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string catalogue = typeof(GpuShaderContracts).Assembly.GetName().Name!;
        string framework = Path.GetFullPath(RuntimeEnvironment.GetRuntimeDirectory());
        pending.Push(typeof(ShaderBuildIdentities).Assembly);
        while (pending.Count != 0)
        {
            var assembly = pending.Pop();
            string name = assembly.GetName().Name!;
            if (name == catalogue || !seen.Add(name)) continue;
            string location = Path.GetFullPath(assembly.Location);
            if (location.StartsWith(framework, StringComparison.OrdinalIgnoreCase)) continue;
            yield return location;
            foreach (var dependency in assembly.GetReferencedAssemblies())
                if (dependency.Name != catalogue) pending.Push(Assembly.Load(dependency));
        }
    }

    /// <summary>Hashes explicit file inputs and records their identities for receipt diagnostics.</summary>
    internal static string Files(string schema, IEnumerable<string> files, ShaderFileHashIndex? index = null,
        Dictionary<string, string>? details = null)
    {
        var values = new List<string> { schema };
        foreach (string path in files.Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            byte[] digest;
            if (index != null) digest = index.GetHash(path);
            else { using var stream = File.OpenRead(path); digest = SHA256.HashData(stream); }
            string value = Convert.ToHexString(digest);
            values.Add(path); values.Add(value);
            if (details != null) details["implementation: " + path] = value;
        }
        return Hash(values.ToArray());
    }

    /// <summary>Frames identity components without delimiter collisions or culture-dependent encoding.</summary>
    internal static string Hash(params string[] values) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    #endregion
}
