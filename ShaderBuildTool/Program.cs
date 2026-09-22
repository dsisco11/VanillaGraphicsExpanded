using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

using TinyPreprocessor;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;

using VanillaGraphicsExpanded;

/// <summary>Builds and validates the complete owned SPIR-V asset catalog.</summary>
internal static class Program
{
    private const string DefaultDomain = "vanillagraphicsexpanded";

    /// <summary>Validates inputs, regenerates stale artifacts and publishes a success receipt last.</summary>
    public static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);

            if (options.ShowHelp)
            {
                Options.PrintHelp();
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.AssetsRoot) || string.IsNullOrWhiteSpace(options.OutputRoot))
            {
                Console.Error.WriteLine("Missing required args. See --help.");
                return 2;
            }

            string assetsRoot = Path.GetFullPath(options.AssetsRoot);
            string outputRoot = Path.GetFullPath(options.OutputRoot);
            string domain = string.IsNullOrWhiteSpace(options.Domain) ? DefaultDomain : options.Domain.Trim();

            // We assume dotnet tool restore has run (MSBuild target does this).

            string domainShadersRoot = Path.Combine(assetsRoot, domain, "shaders");
            if (!Directory.Exists(domainShadersRoot))
            {
                throw new DirectoryNotFoundException("Shader input directory is missing: " + domainShadersRoot);
            }

            ShaderVariantResolver registry = options.RegistryScope switch
            {
                "production" => GpuShaderContracts.Registry,
                "build-validation" => BuildValidationShaderPrograms.Create(),
                "build-validation-graphics" => BuildValidationShaderPrograms.Create(includeCompute: false),
                _ => throw new OptionsException("Unknown registry scope: " + options.RegistryScope)
            };
            string fingerprint = ShaderBuildReceipt.Fingerprint(assetsRoot, domain,
                options.WorkingDirectory ?? Directory.GetCurrentDirectory(), options.TargetEnv, options.WarningsAsErrors) + "|" + options.RegistryScope;
            if (options.Incremental && ShaderBuildReceipt.IsCurrent(outputRoot, fingerprint))
            {
                Console.WriteLine("[SPIR-V] All shader binaries and contracts are current.");
                return 0;
            }
            if (options.Clean && Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }

            Directory.CreateDirectory(outputRoot);

            ShaderVariantBuild.Run(assetsRoot, outputRoot, domain, options.WorkingDirectory ?? Directory.GetCurrentDirectory(), options.TargetEnv, options.WarningsAsErrors, registry);
            ShaderBuildReceipt.Publish(outputRoot, fingerprint);
            return 0;
        }
        catch (OptionsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[SPIR-V] " + ex.Message);
            return 1;
        }
    }

    /// <summary>Maps every supported shader suffix to its compiler stage.</summary>
    internal static string StageFromExtension(string stageExtension)
    {
        return stageExtension switch
        {
            "csh" => "compute",
            "vsh" => "vertex",
            "fsh" => "fragment",
            "gsh" => "geometry",
            "tcsh" => "tesscontrol",
            "tesh" => "tesseval",
            _ => throw new NotSupportedException("Unknown shader stage: " + stageExtension)
        };
    }

    /// <summary>Runs the pinned local shader compiler and relays its diagnostics.</summary>
    internal static int RunDotnetShaderc(
        string workingDir,
        string inputFile,
        string outputFile,
        string stage,
        string targetEnv,
        bool warningsAsErrors, string entryPoint = "main")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile) ?? ".");

        string werror = warningsAsErrors ? " -Werror" : string.Empty;

        // Retain compiler names for diagnostics; publish the compiler binary unchanged.
        const string keepNames = " -g";

        string args =
            $"tool run dotnet-shaderc -- --shader-stage={stage} --entry-point={entryPoint} --target-env={targetEnv}{keepNames} -x=glsl{werror} -o \"{outputFile}\" \"{inputFile}\"";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null)
        {
            throw new InvalidOperationException("Failed to start dotnet process for dotnet-shaderc.");
        }

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();

        proc.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            Console.WriteLine(stdout.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            Console.Error.WriteLine(stderr.TrimEnd());
        }

        return proc.ExitCode;
    }

    private sealed class OptionsException : Exception
    {
        public OptionsException(string message) : base(message) { }
    }

    private sealed class Options
    {
        public bool ShowHelp { get; private init; }
        public string? AssetsRoot { get; private init; }
        public string? OutputRoot { get; private init; }
        public string Domain { get; private init; } = DefaultDomain;
        public string TargetEnv { get; private init; } = "opengl4.5";
        public bool WarningsAsErrors { get; private init; }
        public bool Clean { get; private init; }
        public bool Incremental { get; private init; }
        public string? WorkingDirectory { get; private init; }
        public string RegistryScope { get; private init; } = "production";

        public static Options Parse(string[] args)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a is "--help" or "-h" or "-?" )
                {
                    flags.Add("help");
                    continue;
                }

                if (a.Equals("--warningsAsErrors", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add("warningsAsErrors");
                    continue;
                }

                if (a.Equals("--incremental", StringComparison.OrdinalIgnoreCase))
                { flags.Add("incremental"); continue; }

                if (a.Equals("--clean", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add("clean");
                    continue;
                }

                if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    throw new OptionsException($"Unknown argument: {a}");
                }

                string key;
                string? value;

                int eq = a.IndexOf('=');
                if (eq >= 0)
                {
                    key = a[2..eq];
                    value = a[(eq + 1)..];
                }
                else
                {
                    key = a[2..];
                    if (i + 1 >= args.Length)
                    {
                        throw new OptionsException($"Missing value for --{key}");
                    }
                    value = args[++i];
                }

                dict[key] = value;
            }

            return new Options
            {
                ShowHelp = flags.Contains("help"),
                AssetsRoot = dict.TryGetValue("assetsRoot", out var assets) ? assets : null,
                OutputRoot = dict.TryGetValue("outputRoot", out var outRoot) ? outRoot : null,
                Domain = dict.TryGetValue("domain", out var domain) && !string.IsNullOrWhiteSpace(domain) ? domain : DefaultDomain,
                TargetEnv = dict.TryGetValue("targetEnv", out var env) && !string.IsNullOrWhiteSpace(env) ? env : "opengl4.5",
                WarningsAsErrors = flags.Contains("warningsAsErrors"),
                Clean = flags.Contains("clean"),
                Incremental = flags.Contains("incremental"),
                RegistryScope = dict.TryGetValue("registry", out var scope) ? scope ?? "production" : "production",
                WorkingDirectory = dict.TryGetValue("workingDir", out var wd) ? wd : null
            };
        }

        public static void PrintHelp()
        {
            Console.WriteLine("ShaderBuildTool (VGE)\n");
            Console.WriteLine("Preprocesses VGE GLSL (@import) and compiles all owned shader stages and variants to SPIR-V using dotnet-shaderc (shaderc).\n");
            Console.WriteLine("Required:");
            Console.WriteLine("  --assetsRoot <path>   Path to the mod's assets directory (contains <domain>/shaders/...) ");
            Console.WriteLine("  --outputRoot <path>   Output root for artifacts (e.g. <project>/artifacts/spirv)");
            Console.WriteLine("\nOptional:");
            Console.WriteLine("  --registry <scope>   production, build-validation, or build-validation-graphics");
            Console.WriteLine("  --domain <name>       Asset domain (default: vanillagraphicsexpanded)");
            Console.WriteLine("  --targetEnv <env>     shaderc target env (default: opengl4.5)");
            Console.WriteLine("  --warningsAsErrors    Pass -Werror to compiler");
            Console.WriteLine("  --incremental         Skip compilation only when input and output content matches the last successful build");
            Console.WriteLine("  --clean               Delete outputRoot before building");
            Console.WriteLine("  --workingDir <path>   Working directory (should contain .config/dotnet-tools.json)");
        }
    }
}
