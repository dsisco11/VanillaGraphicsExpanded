using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises closed engine uniform-buffer specializations through actual native uploads.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineGenericStateTests(HeadlessGLFixture fixture)
{
    private static byte[]? capturedPayload;
    #region Public API
    /// <summary>Both virtual and concrete references discover independently patchable value-type bodies.</summary>
    [Fact]
    public void ConcreteUniformUpdatesPublishStorageAndPreserveContentOnlyRevision()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.GenericUniformUpdates." + Guid.NewGuid());
        try
        {
            var definitions = typeof(UBO).GetMethods().Where(method => method.Name == "Update" && method.IsGenericMethodDefinition).ToArray();
            var targets = EngineGenericStateTargets.Discover([typeof(EngineGenericStateTests).Assembly], definitions).ToArray();
            Assert.Equal(4, targets.Length);
            var integrated = EngineStateTargets.Discover([typeof(UBO).Assembly, typeof(EngineGenericStateTests).Assembly]).ToArray();
            Assert.All(targets.Where(target => target.GetParameters().Length == 1), target => Assert.Contains(target, integrated));
            Assert.All(targets.Where(target => target.GetParameters().Length == 3), target => Assert.DoesNotContain(target, integrated));
            Assert.All(targets, target => Assert.False(target.ContainsGenericParameters));
            // Observe bytes passed by the installed engine without changing its pointer semantics.
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.BufferData), [typeof(BufferTarget), typeof(int), typeof(IntPtr), typeof(BufferUsageHint)]), prefix: new HarmonyMethod(typeof(EngineGenericStateTests), nameof(CapturePayload)));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.BufferSubData), [typeof(BufferTarget), typeof(IntPtr), typeof(int), typeof(IntPtr)]), prefix: new HarmonyMethod(typeof(EngineGenericStateTests), nameof(CapturePayload)));
            foreach (var target in targets.Where(target => target.GetParameters().Length == 1).Concat(new[] { typeof(UBO).GetMethod("Bind")!, typeof(UBO).GetMethod("Unbind")! }))
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var first = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); first.Allocate(4);
            using var second = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); second.Allocate(8);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var firstEntry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(first));
            var secondEntry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(second));
            var a = new UBO { Handle = first.BufferId, Size = 4 };
            var b = new UBO { Handle = second.BufferId, Size = 8 };
            ulong firstRevision = firstEntry.MetadataRevision, secondRevision = secondEntry.MetadataRevision;
            // Separate concrete callers retain both static MethodSpecs and genuine virtual dispatch.
            capturedPayload = null; UploadInt(a, 17); var firstPayload = Assert.IsType<byte[]>(capturedPayload);
            capturedPayload = null; UploadPair(b, new Pair { A = 23, B = 29 }); var secondPayload = Assert.IsType<byte[]>(capturedPayload);
            Assert.False(manager.IsResourceMetadataAvailable(first));
            Assert.False(manager.IsResourceMetadataAvailable(second));
            Assert.Same(firstEntry, manager.AcquireResource(GpuResourceKind.Buffer, first.BufferId, new GpuBufferMetadataReader()));
            Assert.Same(secondEntry, manager.AcquireResource(GpuResourceKind.Buffer, second.BufferId, new GpuBufferMetadataReader()));
            var initialOne = new int[1]; var initialTwo = new int[2];
            using (StateCache.Current.BindBufferScope(BufferTarget.CopyReadBuffer, first.BufferId)) GL.GetBufferSubData(BufferTarget.CopyReadBuffer, IntPtr.Zero, 4, initialOne);
            using (StateCache.Current.BindBufferScope(BufferTarget.CopyReadBuffer, second.BufferId)) GL.GetBufferSubData(BufferTarget.CopyReadBuffer, IntPtr.Zero, 8, initialTwo);
            Assert.Equal(firstPayload, MemoryMarshal.AsBytes(initialOne.AsSpan()).ToArray());
            Assert.Equal(secondPayload, MemoryMarshal.AsBytes(initialTwo.AsSpan()).ToArray());
            Assert.Equal(firstRevision + 1, firstEntry.MetadataRevision);
            Assert.Equal(secondRevision + 1, secondEntry.MetadataRevision);
            Assert.Equal((nint)4, firstEntry.Metadata.Capacity);
            Assert.Equal((nint)8, secondEntry.Metadata.Capacity);
            capturedPayload = null; ReplaceInt(a, 31); firstPayload = Assert.IsType<byte[]>(capturedPayload);
            capturedPayload = null; ReplacePair(b, new Pair { A = 37, B = 41 }); secondPayload = Assert.IsType<byte[]>(capturedPayload);
            Assert.Equal(firstRevision + 1, firstEntry.MetadataRevision);
            Assert.Equal(secondRevision + 1, secondEntry.MetadataRevision);
            Assert.False(StateCache.Current.TryGetCachedBoundBuffer(BufferTarget.UniformBuffer, out _));
            Assert.Equal(0, StateCache.Current.GetBoundBuffer(BufferTarget.UniformBuffer));
            var one = new int[1]; var two = new int[2];
            using (StateCache.Current.BindBufferScope(BufferTarget.CopyReadBuffer, first.BufferId)) GL.GetBufferSubData(BufferTarget.CopyReadBuffer, IntPtr.Zero, 4, one);
            using (StateCache.Current.BindBufferScope(BufferTarget.CopyReadBuffer, second.BufferId)) GL.GetBufferSubData(BufferTarget.CopyReadBuffer, IntPtr.Zero, 8, two);
            Assert.Equal(firstPayload, MemoryMarshal.AsBytes(one.AsSpan()).ToArray());
            Assert.Equal(secondPayload, MemoryMarshal.AsBytes(two.AsSpan()).ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            harmony.UnpatchAll(harmony.Id);
            Assert.All(targets, target => Assert.DoesNotContain(Harmony.GetPatchInfo(target)?.Transpilers ?? [], patch => patch.owner == harmony.Id));
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Open generic native operands are recognized before closing their containing caller.</summary>
    [Fact]
    public void GenericNativeOperandProducesConcreteTarget()
    {
        var definition = typeof(EngineGenericStateTests).GetMethod(nameof(GenericNativeUpload), BindingFlags.NonPublic | BindingFlags.Static)!;
        var targets = EngineStateTargets.Discover(typeof(EngineGenericStateTests).Assembly).ToArray();
        var target = definition.MakeGenericMethod(typeof(int));
        Assert.Contains(target, targets);
        Assert.Contains(definition.MakeGenericMethod(typeof(Pair)), targets);
        var original = PatchProcessor.GetOriginalInstructions(target);
        var rewritten = EngineStateSwitchingHook.Transpiler(original).ToArray();
        Assert.DoesNotContain(rewritten, instruction => instruction.operand is MethodInfo method && method.DeclaringType == typeof(GL) && method.Name == nameof(GL.BufferData));
    }

    /// <summary>Concrete generic GL uploads retain the values of distinct managed structs.</summary>
    [Fact]
    public void GenericNativeUploadsPreserveValues()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.GenericNativeUploads." + Guid.NewGuid());
        try
        {
            var definition = typeof(EngineGenericStateTests).GetMethod(nameof(GenericNativeUpload), BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var type in new[] { typeof(int), typeof(Pair) })
                harmony.Patch(definition.MakeGenericMethod(type), transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); buffer.Allocate(8);
            using (StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, buffer.BufferId))
            {
                GenericNativeUpload(new[] { 17, 23 });
                var result = new int[2]; GL.GetBufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, 8, result);
                Assert.Equal(new[] { 17, 23 }, result);
                GenericNativeUpload(new[] { new Pair { A = 31, B = 37 } });
                GL.GetBufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, 8, result);
                Assert.Equal(new[] { 31, 37 }, result);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Shared reference bodies and unresolved forwarding signatures are rejected before patch installation.</summary>
    [Fact]
    public void UnsupportedGenericReferencesFailClosed()
    {
        var assembly = typeof(EngineGenericStateTests).Assembly;
        var shared = typeof(EngineGenericStateTests).GetMethod(nameof(Shared), BindingFlags.NonPublic | BindingFlags.Static)!;
        var unresolved = typeof(EngineGenericStateTests).GetMethod(nameof(Unresolved), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Throws<NotSupportedException>(() => EngineGenericStateTargets.Discover([assembly], [shared]).ToArray());
        Assert.Throws<NotSupportedException>(() => EngineGenericStateTargets.Discover([assembly], [unresolved]).ToArray());
    }
    #endregion

    #region Private
    /// <summary>Copies exactly the native upload bytes without changing the installed engine payload.</summary>
    private static void CapturePayload(int size, IntPtr data)
    {
        if (data == IntPtr.Zero) { capturedPayload = null; return; }
        capturedPayload = new byte[size];
        Marshal.Copy(data, capturedPayload, 0, size);
    }
    /// <summary>Uploads through the public virtual base signature.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UploadInt(UBORef buffer, int value) => buffer.Update(value);
    /// <summary>Uploads through the concrete implementation signature.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UploadPair(UBO buffer, Pair value) => buffer.Update(value);
    /// <summary>Updates existing content through the concrete implementation signature.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReplaceInt(UBO buffer, int value) => buffer.Update(value, 0, 4);
    /// <summary>Updates existing content through the public virtual base signature.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReplacePair(UBORef buffer, Pair value) => buffer.Update(value, 0, 8);
    /// <summary>Provides a distinct value-type specialization with two independently verified words.</summary>
    public struct Pair { public int A; public int B; }
    /// <summary>Supplies a generic native operand without any nongeneric GL calls.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GenericNativeUpload<T>(T[] data) where T : struct => GL.BufferData(BufferTarget.ArrayBuffer, data.Length * Unsafe.SizeOf<T>(), data, BufferUsageHint.StaticDraw);
    /// <summary>Emits a closed caller for the generic native operand fixture.</summary>
    private static void GenericNativeCaller() => GenericNativeUpload(new[] { 1 });
    /// <summary>Supplies a reference-sharing rejection fixture without native operations.</summary>
    private static T Shared<T>(T value) => value;
    /// <summary>Supplies an unresolved generic-owner reference without native operations.</summary>
    private static T Unresolved<T>(T value) => value;
    /// <summary>Emits a concrete reference-type specification for the rejection fixture.</summary>
    private static string SharedCaller() => Shared("sample");
    /// <summary>Emits a generic forwarding specification for the rejection fixture.</summary>
    private static T UnresolvedCaller<T>(T value) => Unresolved(value);
    #endregion
}
