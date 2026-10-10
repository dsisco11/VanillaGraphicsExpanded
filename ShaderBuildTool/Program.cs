using System;
using System.IO;
using System.Linq;

using ShaderBuildTool.Spirv;
using ShaderBuildTool.Generation;
using VanillaGraphicsExpanded.Rendering.Contracts;

using VanillaGraphicsExpanded;

/// <summary>Builds and validates the complete owned SPIR-V asset catalog.</summary>
internal static class Program
{
    private const string DefaultDomain = "vanillagraphicsexpanded";

    #region Public API
    /// <summary>Validates inputs, regenerates stale artifacts and publishes a success receipt last.</summary>
    public static int Main(string[] args)
    {
        try
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
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

            Console.WriteLine($"[SPIR-V] Starting shader build: incremental={options.Incremental}; clean={options.Clean}; verifyContents={options.VerifyContents}; output='{outputRoot}'.");
            Func<ShaderVariantResolver> resolveRegistry = () => options.RegistryScope switch
            {
                "production" => new ShaderVariantResolver(GpuShaderContracts.Registry.Programs.Values.Where(program => !program.Identity.StartsWith("tests/", StringComparison.Ordinal))),
                "tests" => TestShaderPrograms.Create(),
                "build-validation" => BuildValidationShaderPrograms.Create(),
                "build-validation-graphics" => BuildValidationShaderPrograms.Create(includeCompute: false),
                _ => throw new OptionsException("Unknown registry scope: " + options.RegistryScope)
            };
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                ShaderBuildInvocation.RunAsync(assetsRoot, outputRoot, domain,
                    options.WorkingDirectory ?? Directory.GetCurrentDirectory(), options.TargetEnv,
                    options.WarningsAsErrors, resolveRegistry, options.RegistryScope, options.Concurrency,
                    options.Incremental, options.Clean, options.VerifyContents, cancellation.Token).GetAwaiter().GetResult();
            }
            finally { Console.CancelKeyPress -= cancel; }
            Console.WriteLine(FormattableString.Invariant($"[SPIR-V] Build complete; totalElapsedMs={elapsed.Elapsed.TotalMilliseconds:F1}."));
            return 0;
        }
        catch (OptionsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            // MSBuild's terminal logger retains recognized errors in its failure summary.
            // Include the complete exception so IO failures retain their underlying cause.
            Console.Error.WriteLine("error SPIRV001: " + ex);
            return 1;
        }
    }

    #endregion

    #region Private
    /// <summary>Distinguishes command-line usage failures from compiler failures.</summary>
    private sealed class OptionsException : Exception
    {
        /// <summary>Records the invalid option for a usage diagnostic.</summary>
        public OptionsException(string message) : base(message) { }
    }

    /// <summary>Defines asset selection, output policy and the bounded compiler job limit.</summary>
    private sealed class Options
    {
        public int Concurrency { get; private init; }
        public bool ShowHelp { get; private init; }
        public string? AssetsRoot { get; private init; }
        public string? OutputRoot { get; private init; }
        public string Domain { get; private init; } = DefaultDomain;
        public string TargetEnv { get; private init; } = "opengl4.5";
        public bool WarningsAsErrors { get; private init; }
        public bool Clean { get; private init; }
        public bool Incremental { get; private init; }
        public bool VerifyContents { get; private init; }
        public string? WorkingDirectory { get; private init; }
        public string RegistryScope { get; private init; } = "production";

        #region Command-line parsing
        /// <summary>Parses switches and validates the concurrency limit before any output mutation.</summary>
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
                if (a.Equals("--verifyContents", StringComparison.OrdinalIgnoreCase))
                { flags.Add("verifyContents"); continue; }

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

            int concurrency = Math.Min(8, Environment.ProcessorCount);
            if (dict.TryGetValue("concurrency", out string? requestedConcurrency) &&
                (!int.TryParse(requestedConcurrency, out concurrency) || concurrency < 1))
                throw new OptionsException("--concurrency must be a positive integer; use 1 for serial compilation.");

            return new Options
            {
                Concurrency = concurrency,
                ShowHelp = flags.Contains("help"),
                AssetsRoot = dict.TryGetValue("assetsRoot", out var assets) ? assets : null,
                OutputRoot = dict.TryGetValue("outputRoot", out var outRoot) ? outRoot : null,
                Domain = dict.TryGetValue("domain", out var domain) && !string.IsNullOrWhiteSpace(domain) ? domain : DefaultDomain,
                TargetEnv = dict.TryGetValue("targetEnv", out var env) && !string.IsNullOrWhiteSpace(env) ? env : "opengl4.5",
                WarningsAsErrors = flags.Contains("warningsAsErrors"),
                Clean = flags.Contains("clean"),
                Incremental = flags.Contains("incremental"),
                VerifyContents = flags.Contains("verifyContents"),
                RegistryScope = dict.TryGetValue("registry", out var scope) ? scope ?? "production" : "production",
                WorkingDirectory = dict.TryGetValue("workingDir", out var wd) ? wd : null
            };
        }

        /// <summary>Prints build policy and serial/parallel invocation options.</summary>
        public static void PrintHelp()
        {
            Console.WriteLine("ShaderBuildTool (VGE)\n");
            Console.WriteLine("Preprocesses VGE GLSL (@import) and compiles all owned shader stages and variants to SPIR-V using dotnet-shaderc (shaderc).\n");
            Console.WriteLine($"Shader optimization: {ShaderCompilerProcess.OptimizationArgument} (performance optimization in Debug and Release).\n");
            Console.WriteLine($"Shader debug information: {ShaderCompilerProcess.GenerateDebugInfo} (Debug only).\n");
            Console.WriteLine("Required:");
            Console.WriteLine("  --assetsRoot <path>   Path to the mod's assets directory (contains <domain>/shaders/...) ");
            Console.WriteLine("  --outputRoot <path>   Output root for artifacts (e.g. <project>/artifacts/spirv)");
            Console.WriteLine("\nOptional:");
            Console.WriteLine("  --concurrency <count>        Maximum concurrent compiler jobs (default: min(8, logical CPU count); 1 is serial)");
            Console.WriteLine("  --registry <scope>   production, build-validation, build-validation-graphics, or tests");
            Console.WriteLine("  --domain <name>       Asset domain (default: vanillagraphicsexpanded)");
            Console.WriteLine("  --targetEnv <env>     shaderc target env (default: opengl4.5)");
            Console.WriteLine("  --warningsAsErrors    Unsupported by pinned dotnet-shaderc 1.2.2; fails explicitly");
            Console.WriteLine("  --incremental         Reuse verified source, variant and interface records; check the complete receipt before publication");
            Console.WriteLine("  --clean               Delete outputRoot before building");
            Console.WriteLine("  --verifyContents      Rehash every input instead of trusting unchanged file metadata");
            Console.WriteLine("  --workingDir <path>   Working directory (should contain .config/dotnet-tools.json)");
        }
        #endregion
    }
    #endregion
}
