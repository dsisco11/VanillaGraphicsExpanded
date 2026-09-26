using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies real built shader assets to production programs without starting the game renderer.</summary>
internal sealed class BinaryShaderApiFixture : IDisposable
{
    public Dictionary<string, byte[]> Overrides { get; } = new(StringComparer.Ordinal);
    public List<string> Logs { get; } = [];
    public List<string> Reads { get; } = [];
    public Action<string>? BeforeRead { get; set; }
    public List<Action> ScheduledTasks { get; } = [];
    public ICoreClientAPI Api { get; }
    public Dictionary<string, IShaderProgram> RegisteredPrograms { get; } = new(StringComparer.Ordinal);

    #region API construction
    /// <summary>Routes asset reads to the test output and allows isolated in-memory edits for reload scenarios.</summary>
    public BinaryShaderApiFixture()
    {
        var assets = Proxy<IAssetManager>((method, args) =>
        {
            if (method.Name != "TryGet") throw new NotSupportedException(method.Name);
            var location = (AssetLocation)args![0]!;
            Reads.Add(location.Path);
            BeforeRead?.Invoke(location.Path);
            string path = Path.Combine(AppContext.BaseDirectory, "assets", location.Path);
            if (!Overrides.TryGetValue(location.Path, out var data))
            {
                if (!File.Exists(path)) return null;
                data = File.ReadAllBytes(path);
            }
            return Proxy<IAsset>((member, _) => member.Name switch
            {
                "get_Data" => data,
                "ToText" => Encoding.UTF8.GetString(data),
                "get_Location" => location,
                _ => throw new NotSupportedException(member.Name)
            });
        });
        var logger = Proxy<ILogger>((method, args) =>
        {
            // Logger overloads pack format arguments into an array; retain their error details.
            Logs.Add(method.Name + ": " + string.Join(" ", (args ?? []).SelectMany(
                value => value is object[] values ? values : new[] { value })));
            return null;
        });
        var events = Proxy<IClientEventAPI>((method, args) =>
        {
            if (method.Name != "EnqueueMainThreadTask") throw new NotSupportedException(method.Name);
            ScheduledTasks.Add((Action)args![0]!);
            return null;
        });
        var shaders = Proxy<IShaderAPI>((method, args) =>
        {
            if (method.Name == "NewShader") return new Vintagestory.Client.NoObf.Shader();
            if (method.Name == "RegisterMemoryShaderProgram")
            {
                string name = (string)args![0]!;
                var program = (IShaderProgram)args[1]!;
                if (RegisteredPrograms.TryGetValue(name, out var previous)) previous.Dispose();
                RegisteredPrograms[name] = program;
                return method.ReturnType == typeof(bool) ? true : method.ReturnType == typeof(int) ? RegisteredPrograms.Count : null;
            }
            if (method.Name == "GetProgramByName")
                return RegisteredPrograms.GetValueOrDefault((string)args![0]!);
            throw new NotSupportedException(method.Name);
        });
        Api = Proxy<ICoreClientAPI>((method, _) => method.Name switch
        {
            "get_Assets" => assets,
            "get_Shader" => shaders,
            "get_Event" => events,
            "get_Logger" => logger,
            "get_Side" => EnumAppSide.Client,
            _ => throw new NotSupportedException(method.Name)
        });
        VanillaGraphicsExpanded.PBR.ShaderImportsSystem.Instance.Initialize(Api);
    }

    /// <summary>Releases the fixture-owned import resolver so later tests do not retain its assets.</summary>
    public void Dispose()
    {
        VanillaGraphicsExpanded.LumOn.LumOnDebugShaderProgramFamily.Dispose(Api);
        VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Dispose(Api);
        foreach (var program in RegisteredPrograms.Values) program.Dispose();
        RegisteredPrograms.Clear();
        VanillaGraphicsExpanded.PBR.ShaderImportsSystem.Instance.Clear();
    }

    /// <summary>Builds an interface adapter with explicit behavior for each supported operation.</summary>
    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        T proxy = DispatchProxy.Create<T, AssetApiProxy>();
        ((AssetApiProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    /// <summary>Dispatches fixture API operations without implementing unrelated engine functionality.</summary>
    public class AssetApiProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } = null!;

        /// <summary>Forwards the operation to the fixture's explicitly configured handler.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
    }
    #endregion
}
