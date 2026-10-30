using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Reproduces unrelated registry traversal during successful equal metadata publications.</summary>
[Trait("Category", "Unit")]
public sealed class ResourcePublicationTraversalTests(ITestOutputHelper output)
{
    #region Public API
    /// <summary>Equal VAO facts must not visit registrations that have no dependency on that array.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(32768)]
    public void EqualPublicationDoesNotTraverseUnrelatedRegistrations(int unrelatedRegistrations)
    {
        using var manager = new RHIResourceManager(static () => (1, 1), static _ => true);
        for (int index = 0; index < unrelatedRegistrations; index++)
            manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, index + 1);
        var source = manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, 1);
        manager.PublishMetadata(source, default, GpuMetadataChange.None);
        ulong revision = source.MetadataRevision;
        const int publications = 8;
        using var capture = new RecoveryTraversalCapture(manager, source);
        for (int index = 0; index < publications; index++)
        {
            manager.PublishMetadata(source, default, GpuMetadataChange.Metadata);
            Assert.True(source.IsUsable);
            Assert.True(source.HasCommittedMetadata);
            Assert.False(source.CompletionInProgress);
            Assert.Equal(revision, source.MetadataRevision);
        }
        output.WriteLine($"unrelatedRegistrations={unrelatedRegistrations}; successfulEqualPublications={publications}; "
            + $"dependencyVisits={capture.TotalVisits}; unrelatedVisits={capture.UnrelatedVisits}; revision={revision}");
        Assert.Equal(0, capture.UnrelatedVisits);
    }
    /// <summary>A real dependent is visited while unrelated registrations never enter recovery traversal.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1024)]
    [InlineData(32768)]
    public void RecoveryVisitsOnlyTheOriginalSourcesDependent(int unrelatedRegistrations)
    {
        using var manager = new RHIResourceManager(static () => (1, 1), static _ => true);
        for (int index = 0; index < unrelatedRegistrations; index++)
            manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, index + 1);
        var source = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 1);
        manager.PublishMetadata(source, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var dependent = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 1);
        manager.PublishMetadata(dependent, new(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(1, TextureFormat.R32f, 0, 64, false)), GpuMetadataChange.None);
        source.Withdraw();
        Assert.False(dependent.IsUsable);
        using var capture = new RecoveryTraversalCapture(manager, dependent);
        manager.PublishMetadata(source, source.Metadata, GpuMetadataChange.None);
        Assert.True(dependent.IsUsable);
        Assert.Equal(1, capture.TotalVisits);
        Assert.Equal(0, capture.UnrelatedVisits);
        output.WriteLine($"unrelatedRegistrations={unrelatedRegistrations}; dependencyVisits={capture.TotalVisits}");
    }
    #endregion

    /// <summary>Counts actual loop iterations in the manager method without changing production source or native calls.</summary>
    private sealed class RecoveryTraversalCapture : IDisposable
    {
        [ThreadStatic]
        private static RecoveryTraversalCapture? active;
        private readonly RHIResourceManager manager;
        private readonly GpuResourceRegistration source;
        private readonly Harmony harmony = new("Tests.ResourcePublicationTraversal." + Guid.NewGuid());

        #region Internal API
        internal int TotalVisits { get; private set; }
        internal int UnrelatedVisits { get; private set; }

        /// <summary>Installs one scoped observer of the existing recovery loop on the current test thread.</summary>
        internal RecoveryTraversalCapture(RHIResourceManager manager, GpuResourceRegistration source)
        {
            if (active is not null) throw new InvalidOperationException("Traversal capture is already active.");
            this.manager = manager;
            this.source = source;
            try
            {
                harmony.Patch(AccessTools.Method(typeof(RHIResourceManager), "RecoverResourceDependencies"),
                    transpiler: new HarmonyMethod(typeof(RecoveryTraversalCapture), nameof(ObserveIterations)));
                active = this;
            }
            catch { harmony.UnpatchAll(harmony.Id); throw; }
        }
        #endregion

        #region Public API
        /// <summary>Restores the original method and stops observing before manager teardown.</summary>
        public void Dispose()
        {
            active = null;
            harmony.UnpatchAll(harmony.Id);
        }
        #endregion

        #region Private
        /// <summary>Observes each yielded pair while leaving its value and original control flow on the stack.</summary>
        private static IEnumerable<CodeInstruction> ObserveIterations(IEnumerable<CodeInstruction> instructions)
        {
            int sites = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (instruction.operand is not MethodInfo method || method.Name != "get_Current"
                    || method.ReturnType != typeof(GpuResourceRegistration)) continue;
                sites++;
                yield return new CodeInstruction(OpCodes.Dup);
                yield return new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(RecoveryTraversalCapture), nameof(RecordVisit)));
            }
            if (sites != 1) throw new InvalidOperationException($"Expected one dependency iteration site, found {sites}.");
        }

        /// <summary>Counts only the selected manager's visits, excluding the expected direct dependent.</summary>
        private static void RecordVisit(GpuResourceRegistration entry)
        {
            if (active is not { } capture || !ReferenceEquals(entry.Manager, capture.manager)) return;
            capture.TotalVisits++;
            if (!ReferenceEquals(entry, capture.source)) capture.UnrelatedVisits++;
        }
        #endregion
    }
}
