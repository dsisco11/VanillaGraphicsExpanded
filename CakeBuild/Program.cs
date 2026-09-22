using System;
using System.Collections.Generic;
using System.IO;
using Path = System.IO.Path;
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

            // Overlay the verified binary catalog into the packaged assets.
            // Artifacts mirror the assets tree: artifacts/spirv/<domain>/... -> Releases/<mod>/assets/<domain>/...
            if (context.DirectoryExists($"../{BuildContext.ProjectName}/artifacts/spirv"))
            {
                context.CopyDirectory($"../{BuildContext.ProjectName}/artifacts/spirv/vanillagraphicsexpanded", $"../Releases/{context.Name}/assets/vanillagraphicsexpanded");
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
                throw new InvalidOperationException("Packaging requires built SPIR-V assets.");
            }

            string workspace = Path.GetFullPath(Path.Combine(context.Environment.WorkingDirectory.FullPath, ".."));
            string project = Path.Combine(workspace, "ShaderBuildTool", "ShaderBuildTool.csproj");
            string assets = Path.Combine(workspace, BuildContext.ProjectName, "assets");
            string output = Path.Combine(workspace, BuildContext.ProjectName, "artifacts", "spirv");
            int exit = context.StartProcess("dotnet", new ProcessSettings
            {
                WorkingDirectory = workspace,
                Arguments = $"run --no-build --no-restore --configuration {context.BuildConfiguration} --project \"{project}\" -- --clean --incremental --assetsRoot \"{assets}\" --outputRoot \"{output}\" --workingDir \"{workspace}\""
            });
            if (exit != 0) throw new InvalidOperationException("Shader artifact generation failed.");
        }
    }

    [TaskName("Default")]
    [IsDependentOn(typeof(PackageTask))]
    public class DefaultTask : FrostingTask
    {
    }
}
