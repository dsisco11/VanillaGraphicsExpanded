using System.Reflection;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models engine registration and stage dispatch while leaving renderer work in production callbacks.</summary>
internal sealed class RuntimeRenderEvents
{
    /// <summary>Identifies one renderer callback registered with the controlled engine.</summary>
    internal sealed record Registration(IRenderer Renderer, EnumRenderStage Stage, string Name);
    private readonly Dictionary<string, Delegate?> subscriptions = new();
    public List<Registration> Registrations { get; } = [];
    public List<string> Executed { get; } = [];
    public Queue<Action> MainThreadTasks { get; } = new();
    public IClientEventAPI Api { get; }

    /// <summary>Returns the number of active delegates retained for an engine event.</summary>
    public int SubscriptionCount(string eventName) => subscriptions.GetValueOrDefault(eventName)?.GetInvocationList().Length ?? 0;

    #region Engine adapter
    /// <summary>Creates the event interface without starting a window or game loop.</summary>
    public RuntimeRenderEvents() => Api = Adapt<IClientEventAPI>(Invoke);

    /// <summary>Records actual registrations and subscriptions, rejecting unsupported engine behavior.</summary>
    private object? Invoke(MethodInfo method, object?[]? args)
    {
        if (method.Name.StartsWith("add_", StringComparison.Ordinal))
        {
            string key = method.Name[4..];
            subscriptions.TryGetValue(key, out var old);
            subscriptions[key] = Delegate.Combine(old, (Delegate)args![0]!);
            return null;
        }
        if (method.Name.StartsWith("remove_", StringComparison.Ordinal))
        {
            string key = method.Name[7..];
            subscriptions.TryGetValue(key, out var old);
            subscriptions[key] = Delegate.Remove(old, (Delegate)args![0]!);
            return null;
        }
        switch (method.Name)
        {
            case "RegisterRenderer": Registrations.Add(new((IRenderer)args![0]!, (EnumRenderStage)args[1]!, (string)args[2]!)); return null;
            case "UnregisterRenderer": Registrations.RemoveAll(r => ReferenceEquals(r.Renderer, args![0]) && r.Stage == (EnumRenderStage)args[1]!); return null;
            case "EnqueueMainThreadTask": MainThreadTasks.Enqueue((Action)args![0]!); return null;
            default: throw new NotSupportedException("Runtime events: " + method.Name);
        }
    }

    /// <summary>Runs exactly the callbacks the renderer registered, in engine render-order order.</summary>
    public void Render(EnumRenderStage stage)
    {
        while (MainThreadTasks.TryDequeue(out var action)) action();
        foreach (var registration in Registrations.Where(r => r.Stage == stage).OrderBy(r => r.Renderer.RenderOrder).ToArray())
        {
            Executed.Add(registration.Name);
            registration.Renderer.OnRenderFrame(1f / 60, stage);
        }
    }

    /// <summary>Delivers world teardown to all actual event subscribers.</summary>
    public void LeaveWorld() => subscriptions.GetValueOrDefault("LeaveWorld")?.DynamicInvoke();

    /// <summary>Creates a strict reusable interface edge for engine services.</summary>
    public static T Adapt<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    {
        T value = DispatchProxy.Create<T, EngineProxy>();
        ((EngineProxy)(object)value).Handler = invoke;
        return value;
    }

    /// <summary>Forwards interface operations to the scenario's explicit engine adapter.</summary>
    public class EngineProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        /// <summary>Delegates one engine operation.</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    #endregion
}
