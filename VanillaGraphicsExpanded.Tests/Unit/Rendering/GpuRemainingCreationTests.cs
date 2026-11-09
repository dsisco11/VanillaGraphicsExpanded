using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks empty-schema creation, native realization, and ownership handoff for remaining resource families.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuRemainingCreationTests(HeadlessGLFixture fixture, Xunit.ITestOutputHelper output)
{
    private static int queries;
    #region Public API


    /// <summary>Unsupported extension requests reject before allocating or publishing an identity.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsupportedExtensionsRejectCreation(bool registered)
    {
        // Arrange: Optionally register disposal, then replace only the context's admitted extension projection.
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        var capabilities = context.Capabilities;
        var capabilitySource = AccessTools.Field(typeof(RHICapabilities), "source");
        var original = Assert.IsType<GraphicsCapabilities>(capabilitySource.GetValue(capabilities));
        try
        {
            // Fault the admitted projection used by creation, preserving global discovery facts.
            // A newly admitted context legitimately captures the real device's capabilities again.
            capabilitySource.SetValue(capabilities,
                original with { SupportsExtSemaphore = false, SupportsExtMemoryObject = false });
            Assert.False(capabilities.ExternalSemaphores);
            Assert.False(capabilities.ExternalMemory);
            // Act and assert: Unsupported requests fail before native errors or usable resources appear.
            Assert.Throws<NotSupportedException>(() => context.CreateSemaphore(new()).RequireResource());
            Assert.Throws<NotSupportedException>(() => context.CreateMemoryObject(new()).RequireResource());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            capabilitySource.SetValue(capabilities, original);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Private
    /// <summary>Records actual deletion dispatch, including generated names that IsObject cannot distinguish from deleted names.</summary>
    private sealed unsafe class DeletionObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        internal static readonly HashSet<(string, nint)> Deleted = new();
        #region Public API
        /// <summary>Observes array and native-width deletion entrypoints in both binding tables.</summary>
        public DeletionObservation()
        {
            Deleted.Clear();
            foreach (var (symbol, label) in new[] {
                ("glDeleteVertexArrays", "DeleteVertexArray"), ("glDeleteProgramPipelines", "DeleteProgramPipeline"),
                ("glDeleteSamplers", "DeleteSampler"), ("glDeleteQueries", "DeleteQuery"),
                ("glDeleteTransformFeedbacks", "DeleteTransformFeedback"),
                ("glDeleteSemaphoresEXT", "DeleteSemaphore"), ("glDeleteMemoryObjectsEXT", "DeleteMemoryObject") })
            {
                nint original = native.Original(symbol);
                if (original == 0) continue;
                native.Add(symbol, new DeleteMany((count, ids) =>
                {
                    native.Observe(() => Record(label, count, ids));
                    ((delegate* unmanaged[Stdcall]<int, uint*, void>)original)(count, ids);
                }));
            }
            nint sync = native.Original("glDeleteSync");
            native.Add("glDeleteSync", new DeleteSync(id =>
            {
                native.Observe(() => Deleted.Add(("DeleteSync", id)));
                ((delegate* unmanaged[Stdcall]<nint, void>)sync)(id);
            }));
            native.Install();
        }
        /// <summary>Restores ordinary loading after the creation failure assertions.</summary>
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Copies each deleted name before the native caller releases its argument storage.</summary>
        private static void Record(string name, int count, uint* ids)
        {
            for (int i = 0; i < count; i++) Deleted.Add((name, unchecked((int)ids[i])));
        }
        /// <summary>Matches native object-array deletion.</summary>
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate void DeleteMany(int count, uint* ids);
        /// <summary>Matches full-width native synchronization deletion.</summary>
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate void DeleteSync(nint id);
        #endregion
    }

    /// <summary>Observes descriptor and existence queries only during controlled creation.</summary>
    private sealed class CreationQueryObservation : IDisposable
    {
        private readonly Harmony harmony = new("Tests.RemainingCreationQueries." + Guid.NewGuid());
        /// <summary>Installs observation on the native query overloads without changing dispatch.</summary>
        public CreationQueryObservation()
        {
            queries = 0;
            string[] names = ["GetSamplerParameter", "GetQuery", "GetQueryObject", "GetSync", "GetProgramPipeline", "GetVertexArray", "IsVertexArray", "IsProgramPipeline", "IsSampler", "IsQuery", "IsTransformFeedback", "IsSync"];
            foreach (var method in new[] { typeof(GL), typeof(OpenTK.Graphics.OpenGL4.GL) }.SelectMany(type => type.GetMethods()).Where(method => names.Contains(method.Name) && !method.ContainsGenericParameters))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CreationQueryObservation), nameof(Count)));
            foreach (var method in typeof(GL.Ext).GetMethods().Where(method => new[] { "IsSemaphore", "IsMemoryObject", "GetSemaphoreParameter", "GetMemoryObjectParameter" }.Contains(method.Name) && !method.ContainsGenericParameters))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CreationQueryObservation), nameof(Count)));
        }
        /// <summary>Counts the observed native call.</summary>
        private static void Count() => queries++;
        /// <summary>Removes all hooks owned by this observation.</summary>
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }

    /// <summary>Injects failure after native allocation while retaining pointer-width identity evidence.</summary>
    private sealed class FailedRecipient<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        private nint id;
        internal nint Attempted;
        /// <inheritdoc />
        public override nint ResourceId
        {
            get => id;
            protected set
            {
                id = value;
                if (value != 0) { Attempted = value; throw new InvalidOperationException("Injected identity failure."); }
            }
        }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}


