using System;
using System.Reflection;
using System.Threading;

using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class BlockAccessorWorldProbeTraceSceneTests
{
    [Fact]
    public void Trace_RejectsZeroDirection()
    {
        var blockAccessor = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);

        bool hit = scene.Trace(new Vec3d(0, 0, 0), new Vec3f(0, 0, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);

        Assert.False(hit);
        Assert.Equal(default, traceHit);
    }

    private static class NullObjectProxy
    {
        public static T Create<T>() where T : class
        {
            object proxy = DispatchProxy.Create<T, NullObjectDispatchProxy>();
            return (T)proxy;
        }

        private class NullObjectDispatchProxy : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                if (targetMethod is null) return null;

                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(void)) return null;

                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }
        }
    }
}
