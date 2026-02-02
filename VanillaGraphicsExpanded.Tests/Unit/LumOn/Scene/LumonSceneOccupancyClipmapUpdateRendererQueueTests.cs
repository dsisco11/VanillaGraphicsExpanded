using System;
using System.Reflection;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class LumonSceneOccupancyClipmapUpdateRendererQueueTests
{
    [Fact]
    public void EnqueueRegion_RejectsNegativeY()
    {
        ICoreClientAPI capi = FunctionalCoreClientApiProxy.Create();
        var cfg = new VgeConfig();

        using var renderer = new LumonSceneOccupancyClipmapUpdateRenderer(capi, cfg);

        MethodInfo? enqueue = typeof(LumonSceneOccupancyClipmapUpdateRenderer)
            .GetMethod("EnqueueRegion", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(enqueue);

        object[] args = { new VectorInt3(0, -1, 0), 0 };
        bool ok = (bool)enqueue!.Invoke(renderer, args)!;
        Assert.False(ok);
    }

    private class FunctionalCoreClientApiProxy : DispatchProxy
    {
        private IClientEventAPI? clientEvents;
        private IEventAPI? commonEvents;

        public static ICoreClientAPI Create()
        {
            object obj = Create<ICoreClientAPI, FunctionalCoreClientApiProxy>();
            var proxy = (FunctionalCoreClientApiProxy)obj;
            proxy.clientEvents = FunctionalEventsProxy.CreateClientEvents();
            proxy.commonEvents = FunctionalEventsProxy.CreateCommonEvents();
            return (ICoreClientAPI)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            if (targetMethod.Name == "get_Event")
            {
                // ICoreClientAPI.Event (IClientEventAPI) and ICoreAPI.Event (IEventAPI) share the same name.
                if (targetMethod.ReturnType == typeof(IClientEventAPI)) return clientEvents;
                if (targetMethod.ReturnType == typeof(IEventAPI)) return commonEvents;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalEventsProxy : DispatchProxy
    {
        public static IClientEventAPI CreateClientEvents()
            => (IClientEventAPI)Create<IClientEventAPI, FunctionalEventsProxy>();

        public static IEventAPI CreateCommonEvents()
            => (IEventAPI)Create<IEventAPI, FunctionalEventsProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            _ = args;
            if (targetMethod is null) return null;

            // Swallow all registrations/event subscriptions; tests only need construction-time wiring.
            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
