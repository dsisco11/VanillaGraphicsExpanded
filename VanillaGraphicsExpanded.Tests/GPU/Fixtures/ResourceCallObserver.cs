using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes native binding and texture metadata entry points without replacing their behavior.</summary>
internal sealed class ResourceCallObserver : IDisposable
{
    private readonly Harmony harmony = new("Tests.ResourceCallObserver." + Guid.NewGuid());
    private static ResourceCallObserver? active;
    private readonly Dictionary<string, int> calls = new();
    private readonly bool enabled;
    private readonly Dictionary<GetTextureParameter, int> parameters = new();

    #region Public API
    #region Acquisition
    /// <summary>Installs counters for the concrete integer overloads used by the measured resource paths.</summary>
    internal ResourceCallObserver(bool forceEnabled = false)
    {
        enabled = forceEnabled || Environment.GetEnvironmentVariable("VGE_RESOURCE_BASELINE") == "1";
        if (!enabled) return;
        Assert.Null(active);
        active = this;
        foreach (var method in typeof(GL).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var parameters = method.GetParameters();
            bool bind = method.Name == "BindTexture" && parameters.Length == 2 && parameters[1].ParameterType == typeof(int);
            bool query = method.Name is "GetTextureParameter" or "GetTexParameter" or "GetTextureLevelParameter" or "GetTexLevelParameter"
                && parameters[^1].ParameterType == typeof(int).MakeByRefType()
                && parameters[0].ParameterType != typeof(uint);
            bool validity = method.Name == "IsTexture" && parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
            if (bind || query || validity) harmony.Patch(method, prefix: new HarmonyMethod(typeof(ResourceCallObserver), nameof(Count)));
        }
    }

    #endregion
    #region Measurements
    /// <summary>Starts an isolated interval after setup or readback.</summary>
    internal void Reset() { calls.Clear(); parameters.Clear(); }

    /// <summary>Returns intercepted calls for one concrete native entry point.</summary>
    internal int Calls(string name) => calls.GetValueOrDefault(name);

    /// <summary>Returns the number of native queries for one object parameter.</summary>
    internal int ParameterCalls(GetTextureParameter parameter) => parameters.GetValueOrDefault(parameter);

    /// <summary>Checks observer interception with one known bind and query before accepting measurements.</summary>
    internal void Calibrate(int texture)
    {
        if (!enabled) return;
        int previous = GL.GetInteger(GetPName.TextureBinding2D);
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.GetTexLevelParameter(TextureTarget.Texture2D, 0, GetTextureParameter.TextureWidth, out int width);
            GL.GetTextureParameter(texture, GetTextureParameter.TextureTarget, out int target);
            GL.GetTextureLevelParameter(texture, 0, GetTextureParameter.TextureWidth, out int directWidth);
            Assert.True(width > 0);
            Assert.Equal(width, directWidth);
            Assert.Equal((int)TextureTarget.Texture2D, target);
            Assert.Equal(1, calls.GetValueOrDefault("BindTexture"));
            Assert.Equal(1, calls.GetValueOrDefault("GetTexLevelParameter"));
            Assert.Equal(1, calls.GetValueOrDefault("GetTextureParameter"));
            Assert.Equal(1, calls.GetValueOrDefault("GetTextureLevelParameter"));
        }
        finally { GL.BindTexture(TextureTarget.Texture2D, previous); Reset(); }
    }

    /// <summary>Persists exact entry-point counts for a named reproducible interval.</summary>
    internal void Record(string scenario)
    {
        if (!enabled) return;
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/gpu-resource-handles-baseline"));
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, scenario + ".counts.txt"),
            new[] { $"GPU={GL.GetString(StringName.Renderer)}", $"GL={GL.GetString(StringName.Version)}" }
                .Concat(new[] { "BindTexture", "GetTextureParameter", "GetTextureLevelParameter", "GetTexLevelParameter" }
                    .Select(name => $"{name}={calls.GetValueOrDefault(name)}")));
    }

    /// <summary>Retains exact floating-point output bytes outside the measured native-call interval.</summary>
    internal void RecordPixels(string scenario, IEnumerable<float> values)
    {
        if (!enabled) return;
        var pixels = values.ToArray();
        var bytes = new byte[pixels.Length * sizeof(float)];
        System.Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
        string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/gpu-resource-handles-baseline"));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, scenario + ".f32"), bytes);
    }
    #endregion
    #region Cleanup
    /// <summary>Removes only this observer's patches and releases its process-local association.</summary>
    public void Dispose()
    {
        if (!enabled) return;
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion

    #endregion

    #region Private
    /// <summary>Counts the actual intercepted OpenTK entry point before its native call.</summary>
    private static void Count(MethodBase __originalMethod, object[] __args)
    {
        if (active is { } observer)
        {
            observer.calls[__originalMethod.Name] = observer.calls.GetValueOrDefault(__originalMethod.Name) + 1;
            if (__originalMethod.Name is "GetTextureParameter" or "GetTexParameter" && __args[1] is GetTextureParameter parameter)
                observer.parameters[parameter] = observer.parameters.GetValueOrDefault(parameter) + 1;
        }
    }
    #endregion
}
