using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Publishes initialized volume sets and retains the old domain during bounded replacement work.</summary>
internal sealed class CloudVolumeStorage(Func<CloudVolumeMaintenanceProgram> createMaintenance,
    Func<CloudVolumeCopyProgram> createCopy,CloudVolumeSubmission? submission=null) : IDisposable
{
    private readonly CloudVolumeSubmission submission=submission??new();
    /// <summary>Exposes prepared maintenance and copy footprints before update entry.</summary>
    internal System.Collections.Generic.IEnumerable<GpuComputeProgram> SubmissionPrograms=>new GpuComputeProgram[]{maintenance!,copy!};
    private CloudVolumeMaintenanceProgram? maintenance;
    private CloudVolumeCopyProgram? copy;
    private CloudVolumeFields? active, pending;
    private CloudVolumeIdentity? identity, pendingIdentity;
    private Vector3 pendingFallback;
    private (nint Handle, long Generation) context;
    private ulong maintenanceRevision, copyRevision;
    private bool available, disposed;
    /// <summary>Requires fresh geometry while retaining the previous complete domain and drawable.</summary>
    internal bool GeometryReplacement {get;private set;}
    internal bool Ready => available && active is not null && ExecutablesCurrent();
    internal CloudVolumeFields Published => Ready ? active! : throw new InvalidOperationException("Cloud volume is unavailable.");
    internal CloudVolumeFields? Pending => pending;
    internal long StorageBytes => (active?.Domain.Bytes ?? 0) + (pending?.Domain.Bytes ?? 0);

    #region Public API
    #region Admission and initialization
    /// <summary>Checks maximum selected capabilities and owns both maintenance executables before allocation.</summary>
    internal void Prepare()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Validate(new CloudVolumeDomain(200, default, 0, 0), GpuSupport.Graphics);
        ValidateFormats();
        maintenance ??= createMaintenance(); copy ??= createCopy();
        if (!maintenance.EnsureReady() || !copy.EnsureReady()) throw new InvalidOperationException("Cloud volume maintenance is unavailable.");
        var nextContext = RenderContextRegistry.Current();
        ulong nextMaintenance = ((IGpuProgram)maintenance).ExecutableRevision, nextCopy = ((IGpuProgram)copy).ExecutableRevision;
        if (context != nextContext || maintenanceRevision != nextMaintenance || copyRevision != nextCopy) RetireFields();
        context = nextContext; maintenanceRevision = nextMaintenance; copyRevision = nextCopy;
    }

    /// <summary>Admits a replacement once, freezing its domain and fallback while subsequent observations wait.</summary>
    internal void Request(CloudVolumeDomain domain, CloudVolumeIdentity source, CloudVolumeEncoding encoding,
        Vector3 fallbackPhysical, Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!encoding.IsValid) throw new ArgumentOutOfRangeException(nameof(encoding));
        Vector3 fallback = encoding.Encode(fallbackPhysical);
        Prepare(); Validate(domain, GpuSupport.Graphics);
        if (!current()) throw new InvalidOperationException("Cloud input changed before allocation.");
        if ((identity is not null && !identity.Matches(source)) || (pendingIdentity is not null && !pendingIdentity.Matches(source))) RetireFields();
        // An admitted initial fill cannot chase later placements or heights indefinitely.
        if (pending is not null) return;
        if (!GeometryReplacement && Ready && active!.Domain.CanScroll(domain))
        {
            if (active.Domain != domain) Scroll(domain, active.Encoding.Encode(fallbackPhysical), current);
            return;
        }
        if (checked(StorageBytes + domain.Bytes) > CloudVolumeDomain.PeakLimit) throw new NotSupportedException("Cloud volume replacement exceeds the peak payload limit.");
        try
        {
            pending = new CloudVolumeFields(domain, encoding); GeometryReplacement=false; pendingFallback = fallback; pendingIdentity = source;
            if (!current() || !ExecutablesCurrent()) throw new InvalidOperationException("Cloud allocation lost its certificate.");
        }
        catch { RetirePending(); throw; }
    }

    /// <summary>Initializes at most 4096 physical blocks; initialized storage is distinct from generated density.</summary>
    internal bool InitializeNext(Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pending is null) return false;
        if (pending.Initialized) return true;
        int start = pending.InitializedBlocks, count = Math.Min(4096, pending.Domain.Blocks - start);
        try
        {
            RequireCurrent(current);
            RunMaintenance(pending, null, pendingFallback, start, count);
            RequireCurrent(current); pending.InitializedBlocks += count;
            return pending.Initialized;
        }
        catch { RetirePending(); throw; }
    }

    /// <summary>Publishes the complete new field set only after its caller certifies initial density completion.</summary>
    internal bool PublishPending(Func<bool> densityComplete, Func<bool> current)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (pending is null || !pending.Initialized || !densityComplete()) return false;
        try
        {
            RequireCurrent(current);
            var retired = active;
            active = pending; identity = pendingIdentity; pending = null; pendingIdentity = null;
            active.Revision = checked(active.Revision + 1); available = true;
            retired?.Dispose(); return true;
        }
        catch { available = false; RetirePending(); throw; }
    }
    #endregion

    #region Publication and lifetime
    /// <summary>Requests a bounded same-layout regeneration after the prior update has retired.</summary>
    internal void ReplaceGeometry(){RetirePending();GeometryReplacement=true;}
    /// <summary>Withdraws sampling after failed in-place density work until explicit lifecycle recovery.</summary>
    internal void Withdraw() => available = false;

    /// <summary>Repairs and refines a disjoint diffuse destination, publishing only a barrier-complete texture/scale pair.</summary>
    /// <remarks>The refinement owner must submit its producers through the shared submission owner, closing their visibility before returning.</remarks>
    internal bool UpdateDiffuse(CloudVolumeEncoding target, Func<bool> current, Action<CloudVolumeFields> refine,
        Func<bool>? complete = null, GpuIndirectBuffer? commands = null, bool deferPublication = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var fields = Published;
        if (!target.IsValid || Vector3.Max(target.Cap, fields.Encoding.Cap) != target.Cap || target.Scale < fields.Encoding.Scale)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (fields.Revision == uint.MaxValue) throw new InvalidOperationException("Cloud publication revision requires reset.");
        try
        {
            RequireCurrent(current);
            // A previous successful list covers every differing block only when both fields
            // share the destination scale. Generic callbacks have no tracked write footprint.
            bool repair = commands is not null && fields.DiffuseRepairTracked &&
                fields.Encoding.Scale == target.Scale && fields.DiffuseSpareScale == target.Scale;
            copy!.Capture(fields.Domain, fields.Encoding, target, repair); copy.Source = fields.Diffuse;
            copy.Scratch = fields.Scratch;
            copy.Destination = new(fields.DiffuseSpare, TextureAccess.WriteOnly, Layered: true);
            // The lighting selector emits zero groups for an empty diffuse list. No CPU count decision is required.
            submission.Run(copy, () =>
            {
                if (commands is null) copy.Dispatch(fields.Domain.BlocksX, fields.Domain.BlocksY, fields.Domain.BlocksX);
                else copy.DispatchIndirect(commands, repair ? 60 : 48);
            });
            // The gather's submission already closes image/status visibility; do not repeat its barrier.
            refine(fields);
            RequireCurrent(current);
            if (complete is not null && !complete()) throw new InvalidOperationException("Cloud diffuse completion rejected.");
            if (!deferPublication) fields.PublishDiffuse(target);
            return true;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        { available = false; return false; }
    }

    /// <summary>Commits a staged nonempty diffuse destination only after its final fence and status validation.</summary>
    internal void CommitDiffuse(CloudVolumeFields fields, CloudVolumeEncoding target, Func<bool> current)
    {
        RequireCurrent(current);
        if (!Ready || !ReferenceEquals(Published, fields) || !target.IsValid ||
            Vector3.Max(target.Cap, fields.Encoding.Cap) != target.Cap || target.Scale < fields.Encoding.Scale)
            throw new InvalidOperationException("Staged cloud diffuse publication lost its certificate.");
        fields.PublishDiffuse(target, repairTracked: true);
    }

    /// <summary>Revokes retained fields and executable state at explicit lifecycle boundaries.</summary>
    internal void Reset() => RenderingCleanup.Run(RetireFields,
        () => { maintenance?.Dispose(); maintenance = null; }, () => { copy?.Dispose(); copy = null; });

    /// <summary>Releases all owned allocations and programs; repeated disposal is harmless.</summary>
    public void Dispose() { if (disposed) return; disposed = true; Reset(); }

    /// <summary>Admits dimensions, fixed work shapes and complete owned payload before acquiring GPU storage.</summary>
    internal static void Validate(CloudVolumeDomain domain, GraphicsCapabilities caps)
    {
        if (!caps.SupportsArbComputeShader || !caps.SupportsArbShaderStorageBufferObject || !caps.SupportsArbShaderImageLoadStore ||
            !caps.SupportsInternalFormatQuery2 || caps.Max3DTextureSize < Math.Max(domain.Size, CloudVolumeDomain.Height) ||
            caps.MaxTextureSize < domain.Width || caps.MaxImageUnits < 4 || caps.MaxComputeImageUniforms < 4 ||
            caps.MaxComputeTextureImageUnits < 1 || caps.MaxCombinedTextureImageUnits < 1 ||
            caps.MaxUniformBufferBindings < 1 || caps.MaxUniformBlockSize < 80 ||
            caps.MaxShaderStorageBufferBindings < 3 || caps.MaxComputeShaderStorageBlocks < 3 ||
            caps.MaxShaderStorageBlockSize < Math.Max(domain.Blocks * 64L, CloudVolumeDomain.ScratchBytes - 4096) ||
            caps.MaxComputeWorkGroupInvocations < 256 || caps.MaxComputeWorkGroupSize.Length < 3 ||
            caps.MaxComputeWorkGroupCount.Length < 3 || domain.Bytes > CloudVolumeDomain.ResidentLimit)
            throw new NotSupportedException("Cloud volume storage exceeds the context capabilities or payload limit.");
        int[] groupSize = [8, 4, 8], groupCount = [4096, 17, (domain.Size + 3) / 4];
        groupCount[0] = Math.Max(groupCount[0], (domain.Size + 3) / 4);
        for (int axis = 0; axis < 3; axis++)
            if (caps.MaxComputeWorkGroupSize[axis] < groupSize[axis] || caps.MaxComputeWorkGroupCount[axis] < groupCount[axis])
                throw new NotSupportedException("Cloud volume work shape exceeds the context capabilities.");
    }
    #endregion
    #endregion

    #region Private
    #region Maintenance and submission
    /// <summary>Preserves physical slots of overlapping keys and clears only entering or logical guard cells.</summary>
    private void Scroll(CloudVolumeDomain domain, Vector3 fallback, Func<bool> current)
    {
        var fields = active!;
        try
        {
            RequireCurrent(current);
            if (fields.Revision == uint.MaxValue) throw new InvalidOperationException("Cloud publication revision requires reset.");
            // No texture relocation spare: each invocation owns the same physical slot before and after scrolling.
            for (int start = 0; start < domain.Blocks; start += 4096)
                RunMaintenance(fields, domain, fallback, start, Math.Min(4096, domain.Blocks - start));
            RequireCurrent(current); fields.Domain = domain; fields.Revision++;
            // Entering/guard initialization can remove density before the generator compares it.
            // Changed finite coverage also changes transport exits, even for unchanged interior texels.
            fields.DensityState.LayoutChanged=true;fields.DensityState.MaintenanceRequired=true;
        }
        catch { available = false; throw; }
    }

    /// <summary>Stages image/metadata resources and submits one bounded ordered maintenance batch.</summary>
    private void RunMaintenance(CloudVolumeFields fields, CloudVolumeDomain? next, Vector3 fallback, int start, int count)
    {
        var target = next ?? fields.Domain;
        maintenance!.Capture(target, next is null ? null : fields.Domain, fallback, start, count);
        maintenance.Density = new(fields.Density, TextureAccess.ReadWrite, Layered: true);
        maintenance.Direct = new(fields.Direct, TextureAccess.WriteOnly, Layered: true);
        maintenance.Diffuse = new(fields.Diffuse, TextureAccess.WriteOnly, Layered: true);
        maintenance.Spare = new(fields.DiffuseSpare, TextureAccess.WriteOnly, Layered: true);
        maintenance.Regions = fields.Regions; maintenance.Occupancy = fields.Occupancy;maintenance.Scratch=fields.Scratch;
        submission.Run(maintenance, () => maintenance.Dispatch(count));
    }

    #endregion

    #region Admission and cleanup
    /// <summary>Rejects filtered/image formats unsupported by the active driver before allocation.</summary>
    private static void ValidateFormats()
    {
        foreach (var format in new[] { SizedInternalFormat.R16f, SizedInternalFormat.Rg16f, SizedInternalFormat.Rgba16f })
        {
            var caps = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture3D, format);
            if (!caps.Supported || caps.ImageLoadSupport == 0 || caps.ImageStoreSupport == 0 || caps.FilterSupport == 0)
                throw new NotSupportedException("Cloud volume format unavailable: " + format);
        }
        var descriptor = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D, SizedInternalFormat.Rgba32f);
        if (!descriptor.Supported || descriptor.ImageLoadSupport == 0 || descriptor.ImageStoreSupport == 0)
            throw new NotSupportedException("Cloud descriptor storage unavailable.");
    }

    /// <summary>Tests context and exact executable identities without rebuilding during publication.</summary>
    private bool ExecutablesCurrent() => context == RenderContextRegistry.Current() && maintenance is { IsValid: true } &&
        copy is { IsValid: true } && maintenanceRevision == ((IGpuProgram)maintenance).ExecutableRevision &&
        copyRevision == ((IGpuProgram)copy).ExecutableRevision;

    /// <summary>Checks caller publication and executable certificates on both sides of GPU work.</summary>
    private void RequireCurrent(Func<bool> current)
    { if (!current() || !ExecutablesCurrent()) throw new InvalidOperationException("Cloud volume lost its publication certificate."); }

    /// <summary>Retires a partial incoming domain without invalidating a complete old publication.</summary>
    private void RetirePending()
    { var retired = pending; pending = null; pendingIdentity = null; retired?.Dispose(); }

    /// <summary>Revokes sampling before retiring both complete and incomplete field sets.</summary>
    private void RetireFields()
    {
        available = false; GeometryReplacement=false; var retired = active; active = null; identity = null;
        RenderingCleanup.Run(() => retired?.Dispose(), RetirePending);
    }
    #endregion
    #endregion
}
