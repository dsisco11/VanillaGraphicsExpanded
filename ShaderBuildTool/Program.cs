using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using ShaderBuildTool.Spirv;

using TinyPreprocessor;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;

using VanillaGraphicsExpanded;

internal static class Program
{
    private const string DefaultDomain = "vanillagraphicsexpanded";

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
                Console.WriteLine($"[SPIR-V] No shaders directory found: {domainShadersRoot}. Skipping.");
                return 0;
            }

            if (options.Clean && Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }

            Directory.CreateDirectory(outputRoot);

            var shaderFiles = Directory
                .EnumerateFiles(domainShadersRoot, "*.csh", SearchOption.AllDirectories)
                .ToArray();

            if (shaderFiles.Length == 0)
            {
                Console.WriteLine("[SPIR-V] No compute shaders found; skipping.");
                return 0;
            }

            string outDir = Path.Combine(outputRoot, domain, "shaders");
            Directory.CreateDirectory(outDir);

            string tmpRoot = Path.Combine(outputRoot, "_tmp");
            if (Directory.Exists(tmpRoot))
            {
                try
                {
                    Directory.Delete(tmpRoot, recursive: true);
                }
                catch
                {
                }
            }

            Directory.CreateDirectory(tmpRoot);

            var resolver = new FileSystemSyntaxTreeResourceResolver(assetsRoot, domain);
            var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

            int compiled = 0;
            int failed = 0;
            int skipped = 0;

            foreach (string fullPath in shaderFiles)
            {
                string rel = Path.GetRelativePath(domainShadersRoot, fullPath);

                // Skip include-only files under shaders/includes.
                // (Defensive: handles both OS separators and normalized paths.)
                if (rel.Replace('\\', '/').StartsWith("includes/", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                string stageExtension = Path.GetExtension(fullPath).TrimStart('.');
                string shaderName = Path.GetFileNameWithoutExtension(fullPath);
                string sourceName = $"{shaderName}.{stageExtension}";

                try
                {
                    string raw = File.ReadAllText(fullPath);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        skipped++;
                        continue;
                    }

                    string emitted = PreprocessShader(
                        domain: domain,
                        assetsRoot: assetsRoot,
                        sourceName: sourceName,
                        rawSource: raw,
                        preprocessor: preprocessor);

                    string tmpInput = Path.Combine(tmpRoot, sourceName + ".glsl");
                    File.WriteAllText(tmpInput, emitted);

                    string outFile = Path.Combine(outDir, sourceName + ".spv");

                    int exit = RunDotnetShaderc(
                        workingDir: options.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                        inputFile: tmpInput,
                        outputFile: outFile,
                        stage: StageFromExtension(stageExtension),
                        targetEnv: options.TargetEnv,
                        warningsAsErrors: options.WarningsAsErrors);

                    if (exit != 0)
                    {
                        failed++;
                        Console.Error.WriteLine($"[SPIR-V] dotnet-shaderc failed for {sourceName} (exit {exit}).");
                        continue;
                    }

                    compiled++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"[SPIR-V] Failed compiling '{sourceName}': {ex.Message}");
                }
            }

            Console.WriteLine($"[SPIR-V] Done. compiled={compiled}, skipped={skipped}, failed={failed}");

            if (failed > 0)
            {
                return 1;
            }

            return 0;
        }
        catch (OptionsException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static string PreprocessShader(
        string domain,
        string assetsRoot,
        string sourceName,
        string rawSource,
        ShaderSyntaxTreePreprocessor preprocessor)
    {
        var parsed = SyntaxTree.Parse(rawSource, GlslSchema.Instance);

        // Build-time compilation doesn't currently inject any build-time defines,
        // but keep the injection mechanism aligned with runtime.
        InjectDefinesAfterVersion(parsed, defines: new Dictionary<string, string?>());

        bool hadImports = parsed.Select(Query.Syntax<GlImportNode>()).Any();

        PreprocessResult<SyntaxTree>? rawResult = null;
        SyntaxTree outputTree = parsed;

        if (hadImports)
        {
            var rootId = new ResourceId($"{domain}:shaders/{sourceName}");
            var preprocessResult = preprocessor.Process(rootId, parsed, context: null, options: null);
            if (!preprocessResult.Success)
            {
                string diagText = string.Join("\n", preprocessResult.Diagnostics.Select(static d => d.ToString() ?? string.Empty));
                throw new InvalidOperationException($"GLSL preprocessing failed for '{sourceName}':\n{diagText}");
            }

            rawResult = preprocessResult;
            outputTree = preprocessResult.Content;

            // Best-effort #line injection for better compiler diagnostics.
            try
            {
                SyntaxTree ContentProvider(ResourceId id)
                {
                    string rawId = id.Path;
                    string path = rawId;
                    int colon = rawId.IndexOf(':');
                    if (colon >= 0)
                    {
                        path = rawId[(colon + 1)..];
                    }

                    // Root (the stage itself)
                    if (path == $"shaders/{sourceName}")
                    {
                        return SyntaxTree.Parse(rawSource, GlslSchema.Instance);
                    }

                    string includePath = Path.Combine(assetsRoot, domain, path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(includePath))
                    {
                        return SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
                    }

                    string includeText = File.ReadAllText(includePath);
                    return SyntaxTree.Parse(includeText, GlslSchema.Instance);
                }

                if (preprocessResult.SourceMap is not null)
                {
                    var injected = LineDirectiveInjector.TryInject(outputTree, preprocessResult.SourceMap, ContentProvider);
                    if (injected.Success)
                    {
                        outputTree = injected.OutputTree;
                    }
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        string emittedUnstripped = outputTree.ToText();
        return SourceCodeImportsProcessor.StripNonAscii(emittedUnstripped);
    }

    private static string StageFromExtension(string stageExtension)
    {
        return stageExtension switch
        {
            "csh" => "compute",
            "vsh" => "vertex",
            "fsh" => "fragment",
            "gsh" => "geometry",
            _ => "compute"
        };
    }

    private static int RunDotnetShaderc(
        string workingDir,
        string inputFile,
        string outputFile,
        string stage,
        string targetEnv,
        bool warningsAsErrors)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputFile) ?? ".");

        string werror = warningsAsErrors ? " -Werror" : string.Empty;

        string args =
            $"tool run dotnet-shaderc -- --shader-stage={stage} --target-env={targetEnv} -x=glsl{werror} -o \"{outputFile}\" \"{inputFile}\"";

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

    private static void InjectDefinesAfterVersion(SyntaxTree tree, IReadOnlyDictionary<string, string?> defines)
    {
        if (defines.Count == 0)
        {
            return;
        }

        string defineBlock = BuildDefineBlock(defines);
        if (string.IsNullOrEmpty(defineBlock))
        {
            return;
        }

        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        bool hasVersion = tree.Select(versionQuery).Any();

        if (!hasVersion)
        {
            throw new InvalidOperationException("Shader source did not contain a #version directive; cannot safely inject defines");
        }

        tree.CreateEditor()
            .InsertAfter(versionQuery, defineBlock)
            .Commit();
    }

    private static string BuildDefineBlock(IReadOnlyDictionary<string, string?> defines)
    {
        if (defines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("\n// VGE: shader defines\n");

        foreach (var (name, value) in defines.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            builder.Append("#define ");
            builder.Append(name.Trim());

            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(' ');
                builder.Append(value.Trim());
            }

            builder.Append('\n');
        }

        builder.Append('\n');
        return builder.ToString();
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
        public string? WorkingDirectory { get; private init; }

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
                WorkingDirectory = dict.TryGetValue("workingDir", out var wd) ? wd : null
            };
        }

        public static void PrintHelp()
        {
            Console.WriteLine("ShaderBuildTool (VGE)\n");
            Console.WriteLine("Preprocesses VGE GLSL (@import) and compiles compute shaders to SPIR-V using dotnet-shaderc (shaderc).\n");
            Console.WriteLine("Required:");
            Console.WriteLine("  --assetsRoot <path>   Path to the mod's assets directory (contains <domain>/shaders/...) ");
            Console.WriteLine("  --outputRoot <path>   Output root for artifacts (e.g. <project>/artifacts/spirv)");
            Console.WriteLine("\nOptional:");
            Console.WriteLine("  --domain <name>       Asset domain (default: vanillagraphicsexpanded)");
            Console.WriteLine("  --targetEnv <env>     shaderc target env (default: opengl4.5)");
            Console.WriteLine("  --warningsAsErrors    Pass -Werror to compiler");
            Console.WriteLine("  --clean               Delete outputRoot before building");
            Console.WriteLine("  --workingDir <path>   Working directory (should contain .config/dotnet-tools.json)");
        }
    }
}
