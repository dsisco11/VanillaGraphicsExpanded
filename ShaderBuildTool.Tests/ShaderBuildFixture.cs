using System.Security.Cryptography;

namespace ShaderBuildTool.Tests;

/// <summary>Owns minimal real shader assets and isolated outputs for reusable compiler integration scenarios.</summary>
internal sealed class ShaderBuildFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "vge-shader-build-" + Guid.NewGuid().ToString("N"));
    public string Assets => Path.Combine(Root, "assets");
    public string Output => Path.Combine(Root, "output");
    public string Shaders => Path.Combine(Assets, "vanillagraphicsexpanded", "shaders");
    public string Repository { get; }

    #region Fixture lifecycle
    /// <summary>Finds the pinned tool manifest and writes valid vertex, fragment and compute inputs.</summary>
    public ShaderBuildFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".config", "dotnet-tools.json")))
            directory = directory.Parent;
        Repository = directory?.FullName ?? throw new DirectoryNotFoundException("Repository tool manifest not found.");
        Directory.CreateDirectory(Shaders);
        File.WriteAllText(Path.Combine(Shaders, "fixture.vsh"), "#version 450 core\nlayout(location=0) in vec3 position; layout(location=0) out vec3 color; void main(){gl_Position=vec4(position,1);color=vec3(1);}");
        File.WriteAllText(Path.Combine(Shaders, "fixture.fsh"), "#version 450 core\n@import \"fixture.inc\"\nlayout(location=0) in vec3 color; layout(location=0) out vec4 result; void main(){result=vec4(color*FACTOR,1);}");
        File.WriteAllText(Path.Combine(Shaders, "fixture.inc"), "#define FACTOR 0.5\n");
        File.WriteAllText(Path.Combine(Shaders, "fixture.csh"), "#version 450 core\nlayout(local_size_x=1) in; layout(std430,binding=0) buffer Data{uint value;}; void main(){value=1;}");
    }

    /// <summary>Deletes only this fixture's unique temporary directory after all compiler work has joined.</summary>
    public void Dispose() => Directory.Delete(Root, recursive: true);
    #endregion

    #region Build observations
    /// <summary>Invokes the normal entry point with an explicit concurrency limit and optional rebuild policy.</summary>
    public int Build(int concurrency, bool clean = true, bool incremental = false)
    {
        var args = new List<string> { "--assetsRoot", Assets, "--outputRoot", Output, "--workingDir", Repository,
            "--registry", "build-validation", "--concurrency", concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (clean) args.Add("--clean");
        if (incremental) args.Add("--incremental");
        return Program.Main(args.ToArray());
    }

    /// <summary>Records exact compiler inputs and published binaries, excluding the build receipt.</summary>
    public SortedDictionary<string, string> ContentSnapshot() => new(Directory.EnumerateFiles(Output, "*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".spv", StringComparison.Ordinal) || path.EndsWith(".glsl", StringComparison.Ordinal))
        .ToDictionary(path => Path.GetRelativePath(Output, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))), StringComparer.Ordinal);
    #endregion
}
