using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cake.Common;
using Cake.Common.Diagnostics;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TinyTokenizer.Ast;
using Vintagestory.API.Common;
using TinyPreprocessor.Core;

using CakeBuild.Spirv;

using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.PBR;

namespace CakeBuild
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            return new CakeHost()
                .UseContext<BuildContext>()
                .Run(args);
        }
    }

    public class BuildContext : FrostingContext
    {
        public const string ProjectName = "VanillaGraphicsExpanded";
        public string BuildConfiguration { get; }
        public string Version { get; }
        public string Name { get; }
        public bool SkipJsonValidation { get; }

        public BuildContext(ICakeContext context)
            : base(context)
        {
            BuildConfiguration = context.Argument("configuration", "Release");
            SkipJsonValidation = context.Argument("skipJsonValidation", false);
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
            Version = modInfo.Version;
            Name = modInfo.ModID;
        }
    }

    [TaskName("ValidateJson")]
    public sealed class ValidateJsonTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            if (context.SkipJsonValidation)
            {
                return;
            }
            var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json");
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file.FullPath);
                    JToken.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
    }

    [TaskName("Build")]
    [IsDependentOn(typeof(ValidateJsonTask))]
    public sealed class BuildTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
                new DotNetCleanSettings
                {
                    Configuration = context.BuildConfiguration
                });


            context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
                new DotNetPublishSettings
                {
                    Configuration = context.BuildConfiguration
                });
        }
    }

    [TaskName("Package")]
    [IsDependentOn(typeof(CompileSpirVShadersTask))]
    public sealed class PackageTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            context.EnsureDirectoryExists("../Releases");
            context.CleanDirectory("../Releases");
            context.EnsureDirectoryExists($"../Releases/{context.Name}");
            context.CopyFiles($"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/publish/*", $"../Releases/{context.Name}");
            if (context.DirectoryExists($"../{BuildContext.ProjectName}/assets"))
            {
                context.CopyDirectory($"../{BuildContext.ProjectName}/assets", $"../Releases/{context.Name}/assets");
            }

            // Overlay SPIR-V artifacts (if any) into the packaged assets.
            // Artifacts mirror the assets tree: artifacts/spirv/<domain>/... -> Releases/<mod>/assets/<domain>/...
            if (context.DirectoryExists($"../{BuildContext.ProjectName}/artifacts/spirv"))
            {
                context.CopyDirectory($"../{BuildContext.ProjectName}/artifacts/spirv", $"../Releases/{context.Name}/assets");
            }

            context.CopyFile($"../{BuildContext.ProjectName}/modinfo.json", $"../Releases/{context.Name}/modinfo.json");
            if (context.FileExists($"../{BuildContext.ProjectName}/modicon.png"))
            {
                context.CopyFile($"../{BuildContext.ProjectName}/modicon.png", $"../Releases/{context.Name}/modicon.png");
            }
            context.Zip($"../Releases/{context.Name}", $"../Releases/{context.Name}_{context.Version}.zip");
        }
    }

    [TaskName("CompileSpirVShaders")]
    [IsDependentOn(typeof(BuildTask))]
    public sealed class CompileSpirVShadersTask : FrostingTask<BuildContext>
    {
        public override void Run(BuildContext context)
        {
            bool enabled = context.Argument("spirv", true);
            if (!enabled)
            {
                context.Information("[SPIR-V] Compilation disabled (spirv=false). Skipping.");
                return;
            }

            string compilerPath = ResolveCompilerPath(context);

            // Print compiler version (best-effort)
            try
            {
                context.StartProcess(
                    compilerPath,
                    new ProcessSettings
                    {
                        Arguments = "--version",
                        RedirectStandardOutput = false,
                        RedirectStandardError = false
                    });
            }
            catch
            {
                // Ignore - some builds may use a shim that doesn't support --version.
            }

            string assetsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(context.Environment.WorkingDirectory.FullPath, "..", BuildContext.ProjectName, "assets"));
            string outputRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(context.Environment.WorkingDirectory.FullPath, "..", BuildContext.ProjectName, "artifacts", "spirv"));

            context.EnsureDirectoryExists(outputRoot);
            context.CleanDirectory(outputRoot);

            var shaderFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/vanillagraphicsexpanded/shaders/**/*.csh");
            if (shaderFiles.Count == 0)
            {
                context.Information("[SPIR-V] No compute shaders found; skipping.");
                return;
            }

            bool warningsAsErrors = context.Argument("spirvWarningsAsErrors", false);

            var resolver = new FileSystemSyntaxTreeResourceResolver(assetsRoot, ShaderImportsSystem.DefaultDomain);
            var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

            int compiled = 0;
            int failed = 0;
            int skipped = 0;

            string tmpRoot = System.IO.Path.Combine(outputRoot, "_tmp");
            context.EnsureDirectoryExists(tmpRoot);

            foreach (var file in shaderFiles)
            {
                string fullPath = file.FullPath;
                string stageExtension = System.IO.Path.GetExtension(fullPath).TrimStart('.');
                string shaderName = System.IO.Path.GetFileNameWithoutExtension(fullPath);
                string sourceName = $"{shaderName}.{stageExtension}";

                // Only compile entrypoints; includes are expected under shaders/includes/*.glsl.
                if (string.Equals(shaderName, "includes", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    string raw = File.ReadAllText(fullPath);
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        skipped++;
                        continue;
                    }

                    SyntaxTree ContentProvider(ResourceId id)
                    {
                        string rawId = id.Path;
                        string path = rawId;
                        int colon = rawId.IndexOf(':');
                        if (colon >= 0)
                        {
                            path = rawId[(colon + 1)..];
                        }

                        if (path == $"shaders/{sourceName}")
                        {
                            return SyntaxTree.Parse(raw, GlslSchema.Instance);
                        }

                        string includePath = System.IO.Path.Combine(assetsRoot, ShaderImportsSystem.DefaultDomain, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                        if (!File.Exists(includePath))
                        {
                            return SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
                        }

                        string includeText = File.ReadAllText(includePath);
                        return SyntaxTree.Parse(includeText, GlslSchema.Instance);
                    }

                    var source = ShaderSourceCode.FromSource(
                        shaderName: shaderName,
                        stageExtension: stageExtension,
                        rawSource: raw,
                        sourceName: sourceName,
                        importPreprocessor: preprocessor,
                        contentProvider: ContentProvider);

                    string tmpInput = System.IO.Path.Combine(tmpRoot, sourceName + ".glsl");
                    File.WriteAllText(tmpInput, source.EmittedSource);

                    string outDir = System.IO.Path.Combine(outputRoot, "vanillagraphicsexpanded", "shaders");
                    context.EnsureDirectoryExists(outDir);
                    string outFile = System.IO.Path.Combine(outDir, sourceName + ".spv");

                    string args = BuildGlslangArgs(tmpInput, outFile, stageExtension, warningsAsErrors);

                    var outputLines = new List<string>();
                    var settings = new ProcessSettings
                    {
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectedStandardOutputHandler = s =>
                        {
                            if (!string.IsNullOrEmpty(s)) outputLines.Add(s);
                            return s ?? string.Empty;
                        },
                        RedirectedStandardErrorHandler = s =>
                        {
                            if (!string.IsNullOrEmpty(s)) outputLines.Add(s);
                            return s ?? string.Empty;
                        }
                    };

                    int exitCode = context.StartProcess(compilerPath, settings);
                    if (exitCode != 0)
                    {
                        failed++;
                        string joined = string.Join(Environment.NewLine, outputLines);
                        throw new Exception($"glslangValidator failed for {sourceName} (exit {exitCode}).\n{joined}");
                    }

                    compiled++;
                }
                catch (Exception ex)
                {
                    failed++;
                    context.Error($"[SPIR-V] Failed compiling '{sourceName}': {ex.Message}");
                }
            }

            context.Information($"[SPIR-V] Done. compiled={compiled}, skipped={skipped}, failed={failed}");

            if (failed > 0)
            {
                throw new Exception($"SPIR-V compilation failed for {failed} shader(s). See build log for details. Disable via --spirv=false");
            }
        }

        private static string ResolveCompilerPath(BuildContext context)
        {
            // Order: Cake arg -> env vars -> PATH fallback
            string arg = context.Argument("glslangValidatorPath", string.Empty) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(arg))
            {
                return arg;
            }

            string? env = context.EnvironmentVariable("VGE_GLSLANG_VALIDATOR")
                ?? context.EnvironmentVariable("GLSLANG_VALIDATOR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env;
            }

            return "glslangValidator";
        }

        private static string BuildGlslangArgs(string inputFile, string outputFile, string stageExtension, bool warningsAsErrors)
        {
            // OpenGL SPIR-V: -G. Force stage via -S to avoid relying on file extension inference.
            // For compute shaders, stage is "comp".
            string stage = stageExtension switch
            {
                "csh" => "comp",
                "vsh" => "vert",
                "fsh" => "frag",
                "gsh" => "geom",
                _ => "comp"
            };

            string werror = warningsAsErrors ? " -Werror" : string.Empty;

            // Quote paths for Windows.
            return $"-G -S {stage}{werror} -o \"{outputFile}\" \"{inputFile}\"";
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(PackageTask))]
    public class DefaultTask : FrostingTask
    {
    }
}