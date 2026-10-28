using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the real OpenTK dispatch boundary independently of caller rewriting.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OpenTKBindingInterceptionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Direct, retained-delegate and generated callers share the native slot after warm-up and reload.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSlotObservesWarmAndGeneratedCallers(bool core)
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core);
        Action<bool> call = core ? OpenTK.Graphics.OpenGL4.GL.DepthMask : GL.DepthMask;
        var generated = new DynamicMethod("LateDepthMask", typeof(void), [typeof(bool)], typeof(OpenTKBindingInterceptionTests).Module);
        var il = generated.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, call.Method);
        il.Emit(OpCodes.Ret);
        var late = (Action<bool>)generated.CreateDelegate(typeof(Action<bool>));
        RuntimeHelpers.PrepareDelegate(call);
        RuntimeHelpers.PrepareDelegate(late);

        call(true);
        late(false);
        call(false);
        Assert.Equal(3, native.Calls);
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        // Equality suppresses managed execution but never either external invocation.
        OpenGlStateCache.Current.SetDepthWriteMask(false);
        Assert.Equal(3, native.Calls);
        native.Reload();
        late(true);
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(4, native.Calls);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Uninstall restores slots immediately while retained callbacks still forward without observation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void UninstallAllowsAlreadyFetchedCallbacks(bool core)
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core);
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        nint fetched = table.Read("glDepthMask");
        nint original = OpenGLBindingInterception.Original(fetched);
        try
        {
            // Uninstall from inside native execution must not wait for this callback to return.
            bool restoredDuringCall = false;
            native.OnCall = () =>
            {
                OpenGLBindingInterception.Uninstall();
                restoredDuringCall = table.Read("glDepthMask") == original;
            };
            ((delegate* unmanaged[Stdcall]<byte, void>)fetched)(0);
            Assert.True(restoredDuringCall);
            Assert.False(OpenGLBindingInterception.ObservationEnabled);
            Assert.Equal(original, table.Read("glDepthMask"));
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            native.OnCall = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            ((delegate* unmanaged[Stdcall]<byte, void>)fetched)(1);
            Assert.Equal(2, native.Calls);
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            native.OnCall = null;
            OpenGLBindingInterception.Install();
            GL.DepthMask(true);
        }
    }

    /// <summary>Restoration does not overwrite a table entry subsequently replaced by another owner.</summary>
    [Fact]
    public void UninstallPreservesLaterTableReplacement()
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core: false);
        var table = OpenGLBindingTable.For(typeof(GL));
        var captured = table.Capture();
        int index = Array.IndexOf(captured.Names, "glDepthMask");
        nint original = OpenGLBindingInterception.Original(captured.Pointers[index]);
        nint replacement = new GLFWBindingsContext().GetProcAddress("glDepthMask");
        try
        {
            Interlocked.Exchange(ref captured.Pointers[index], replacement);
            OpenGLBindingInterception.Uninstall();
            Assert.Equal(replacement, table.Read("glDepthMask"));
        }
        finally
        {
            Interlocked.Exchange(ref captured.Pointers[index], original);
            OpenGLBindingInterception.Install();
        }
    }

    /// <summary>Pre/post observer faults cannot skip or retry dispatch, leak exceptions or retain a stale mask.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservationFaultsStillDispatchAndWithdrawOnlyAffectedKnowledge(bool before)
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core: false);
        var cache = OpenGlStateCache.Current;
        cache.SetDepthWriteMask(true);
        cache.SetDepthFunc(DepthFunction.Greater);
        int baseline = native.Calls;
        var target = before
            ? AccessTools.PropertyGetter(typeof(OpenGLObservationContext), nameof(OpenGLObservationContext.CurrentCache))
            : AccessTools.Method(typeof(OpenGlStateCache), "ObserveExternalDepthMask");
        var harmony = new Harmony("Tests.NativeObserverFault");
        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(OpenTKBindingInterceptionTests), nameof(ThrowObserverFault)));
            GL.DepthMask(false);
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(baseline + 1, native.Calls);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        // A stale known-true value would suppress this repair and leave the driver false.
        cache.SetDepthWriteMask(true);
        Assert.Equal(baseline + 2, native.Calls);
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>A new registered lifetime cannot trigger error-consuming capability discovery from a callback.</summary>
    [Fact]
    public void UncachedContextObservationPreservesNativeErrors()
    {
        EnsureContextValid();
        var owner = new object();
        using var native = new DepthMaskDriver(false);
        try
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(owner, static _ => true);
            Assert.False(VanillaGraphicsExpanded.Rendering.GpuSupport.HasCachedCapabilitiesForCurrentContext);
            GL.DepthFunc((DepthFunction)(-1));
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.False(VanillaGraphicsExpanded.Rendering.GpuSupport.HasCachedCapabilitiesForCurrentContext);
            GL.DepthMask(false);
            Assert.Equal(1, native.Calls);
            VanillaGraphicsExpanded.Rendering.GpuSupport.EnsureCurrentContext();
            GL.DepthMask(false);
            OpenGlStateCache.Current.SetDepthWriteMask(false);
            Assert.Equal(2, native.Calls);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            VanillaGraphicsExpanded.Rendering.GpuSupport.EnsureCurrentContext();
            GC.KeepAlive(owner);
        }
    }
    /// <summary>Partially failed loading preserves immutable originals and can uninstall and reload safely.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void FailedLoaderRetainsSafeCallbacks(bool core)
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core);
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        nint fetched = table.Read("glDepthMask");
        var loader = new CapturedLoader(table, _ => throw new ApplicationException("Injected loader failure."));
        try
        {
            var error = Assert.Throws<ApplicationException>(() => Load(core, loader));
            Assert.Equal("Injected loader failure.", error.Message);
            OpenGLBindingInterception.Uninstall();
            ((delegate* unmanaged[Stdcall]<byte, void>)fetched)(0);
            Assert.Equal(1, native.Calls);
        }
        finally
        {
            native.Reload();
            OpenGLBindingInterception.Install();
        }
        GL.DepthMask(true);
        Assert.Equal(2, native.Calls);
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Concurrent loader completion cannot strand callbacks after uninstall; fetched callbacks remain callable.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void LoadingDuringUninstallPreservesNativeForwarding(bool core)
    {
        EnsureContextValid();
        using var native = new DepthMaskDriver(core);
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        nint fetched = table.Read("glDepthMask");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var loader = new CapturedLoader(table, _ =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Loader test coordination failed.");
        });
        var loading = Task.Run(() => Load(core, loader));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            OpenGLBindingInterception.Uninstall();
            ((delegate* unmanaged[Stdcall]<byte, void>)fetched)(0);
            Assert.Equal(1, native.Calls);
            release.Set();
            loading.GetAwaiter().GetResult();
            foreach (nint pointer in table.Capture().Pointers)
                Assert.Equal(pointer, OpenGLBindingInterception.Original(pointer));
            GL.DepthMask(true);
            Assert.Equal(2, native.Calls);
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            release.Set();
            loading.GetAwaiter().GetResult();
            native.Reload();
            OpenGLBindingInterception.Install();
        }
    }
    #endregion

    #region Private
    /// <summary>Invokes the exact selected loader without requiring a native context on a resolver-only worker.</summary>
    private static void Load(bool core, IBindingsContext loader)
    {
        if (core) OpenTK.Graphics.OpenGL4.GL.LoadBindings(loader);
        else GL.LoadBindings(loader);
    }

    /// <summary>Replays already-resolved addresses while coordinating the selected slot's publication.</summary>
    private sealed class CapturedLoader : IBindingsContext
    {
        private readonly Dictionary<string, nint> addresses = [];
        private readonly Action<string> atDepthMask;
        #region Internal API
        /// <summary>Captures the original driver layer before a resolver is moved to another thread.</summary>
        internal CapturedLoader(OpenGLBindingTable table, Action<string> atDepthMask)
        {
            this.atDepthMask = atDepthMask;
            var snapshot = table.Capture();
            for (int i = 0; i < snapshot.Names.Length; i++)
                addresses.Add(snapshot.Names[i], OpenGLBindingInterception.Original(snapshot.Pointers[i]));
        }
        #endregion
        #region Public API
        /// <inheritdoc />
        public nint GetProcAddress(string name)
        {
            if (name == "glDepthMask") atDepthMask(name);
            return addresses.GetValueOrDefault(name);
        }
        #endregion
    }

    /// <summary>Injects an observation-only failure without replacing or mocking native execution.</summary>
    private static void ThrowObserverFault() => throw new InvalidOperationException("Injected observation failure.");

    /// <summary>Counts actual original dispatch while preserving native effects and the selected loader family.</summary>
    private sealed unsafe class DepthMaskDriver : IBindingsContext, IDisposable
    {
        /// <summary>Preserves the GLboolean ABI of the native counter oracle.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Mask(byte value);
        private static readonly List<Delegate> roots = [];
        private readonly bool core;
        private readonly nint original;
        private readonly nint pointer;
        private readonly GLFWBindingsContext driver = new();

        #region Public API
        /// <summary>Counts driver calls, including backend calls which bypass observation.</summary>
        internal int Calls { get; private set; }
        /// <summary>Exercises lifecycle changes while a real native callback is in progress.</summary>
        internal Action? OnCall { get; set; }
        /// <summary>Decorates the original driver pointer before observational callbacks are installed.</summary>
        internal DepthMaskDriver(bool core)
        {
            this.core = core;
            original = OpenGLBindingInterception.Original(new OpenGLBindingTable(core
                ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL)).Read("glDepthMask"));
            Mask callback = value =>
            {
                Calls++;
                OnCall?.Invoke();
                ((delegate* unmanaged[Stdcall]<byte, void>)original)(value);
            };
            roots.Add(callback);
            pointer = Marshal.GetFunctionPointerForDelegate(callback);
            Reload();
        }
        /// <summary>Reloads the same decorated driver without changing the original-address identity.</summary>
        internal void Reload()
        {
            // Both tables receive the oracle so managed core dispatch is counted as well.
            if (core) { GL.LoadBindings(this); OpenTK.Graphics.OpenGL4.GL.LoadBindings(this); }
            else { OpenTK.Graphics.OpenGL4.GL.LoadBindings(this); GL.LoadBindings(this); }
        }
        /// <inheritdoc />
        public nint GetProcAddress(string procName) => procName == "glDepthMask" ? pointer : driver.GetProcAddress(procName);
        /// <summary>Restores ordinary driver loading so no later test enters this counter oracle.</summary>
        public void Dispose()
        {
            GL.LoadBindings(driver);
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(driver);
        }
        #endregion
    }
    #endregion
}
