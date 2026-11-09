using System;
using System.Numerics;
using System.Reflection;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks admission guards for collision trace inputs.</summary>
public sealed class BlockAccessorWorldProbeTraceSceneTests
{
    #region Public API
    /// <summary>Rejects a zero-length ray before querying the world.</summary>
    [Fact]
    public void Trace_RejectsZeroDirection()
    {
        // Arrange
        var (blockAccessor, accessorProxy) = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act
        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(0, 0, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        // Assert
        Assert.Equal(WorldProbeTraceOutcome.Invalid, outcome);
        Assert.Equal(0, accessorProxy());
        Assert.Equal(default, traceHit);
    }

    /// <summary>Rejects nonpositive maximum distances before querying the world.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Trace_RejectsNonPositiveMaxDistance(double maxDistance)
    {
        // Arrange
        var (blockAccessor, accessorProxy) = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act
        var outcome = scene.Trace(new Vector3d(0, 0, 0), Vector3.UnitX, maxDistance, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        // Assert
        Assert.Equal(WorldProbeTraceOutcome.Invalid, outcome);
        Assert.Equal(0, accessorProxy());
        Assert.Equal(default, traceHit);
    }

    /// <summary>Rejects a NaN direction with an invalid outcome before querying the world.</summary>
    [Fact]
    public void Trace_WhenDirectionContainsNaN_ReturnsInvalid()
    {
        // Arrange
        var (blockAccessor, accessorProxy) = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act
        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(float.NaN, 1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        // Assert
        Assert.Equal(WorldProbeTraceOutcome.Invalid, outcome);
        Assert.Equal(0, accessorProxy());
        Assert.Equal(default, traceHit);
    }

    /// <summary>Rejects an infinite direction with an invalid outcome before querying the world.</summary>
    [Fact]
    public void Trace_WhenDirectionContainsInfinity_ReturnsInvalid()
    {
        // Arrange
        var (blockAccessor, accessorProxy) = NullObjectProxy.Create<IBlockAccessor>();
        var scene = new BlockAccessorWorldProbeTraceScene(blockAccessor);
        // Act
        var outcome = scene.Trace(new Vector3d(0, 0, 0), new Vector3(float.PositiveInfinity, 1, 0), 10, CancellationToken.None, out LumOnWorldProbeTraceHit traceHit);
        // Assert
        Assert.Equal(WorldProbeTraceOutcome.Invalid, outcome);
        Assert.Equal(0, accessorProxy());
        Assert.Equal(default, traceHit);
    }

    #endregion
    #region Private
    /// <summary>Creates a default-returning block accessor that records whether a guard reached it.</summary>
    private static class NullObjectProxy
    {
        #region Public API
        /// <summary>Creates a default-returning accessor and a delegate that reads its invocation count.</summary>
        public static (T Accessor, Func<int> InvocationCount) Create<T>() where T : class
        {
            T accessor = DispatchProxy.Create<T, NullObjectDispatchProxy>();
            var proxy = (NullObjectDispatchProxy)(object)accessor;
            return (accessor, () => proxy.InvocationCount);
        }
        #endregion

        #region Private
        /// <summary>Returns default interface values and counts calls without requiring a live world.</summary>
        private class NullObjectDispatchProxy : DispatchProxy
        {
            #region Public API
            /// <summary>Counts calls so invalid-input tests can prove the accessor was untouched.</summary>
            public int InvocationCount { get; private set; }
            #endregion

            #region Protected
            /// <summary>Returns the default value appropriate to the invoked interface member.</summary>
            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                InvocationCount++;
                if (targetMethod is null) return null;
                Type returnType = targetMethod.ReturnType;
                if (returnType == typeof(void)) return null;
                return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
            }
            #endregion
        }
        #endregion
    }
    #endregion
}
