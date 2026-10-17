using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns coarse cloud height storage and publishes only completed, exact-source GPU reductions.</summary>
internal sealed class CloudHierarchy(Func<CloudHierarchyComputeShader> createProgram) : IDisposable
{
    private CloudHierarchyComputeShader? program;
    private DynamicTexture2D? texture;
    private BorrowedTexture? map;
    private CloudMapInput? allocationSource;
    private CloudHierarchySource? completed;
    private ulong executableRevision;
    private bool failed, disposed;

    #region Public API
    #region Construction and publication
    /// <summary>Describes the current coarse allocation, or is absent when storage is retired.</summary>
    internal CloudHierarchyLayout? Layout { get; private set; }
    /// <summary>Reports the most recent optional-build failure without disabling ordinary cloud rendering.</summary>
    internal string? Failure { get; private set; }

    /// <summary>Withdraws changed contents and retires invalid lifetimes even when other draw inputs are unavailable.</summary>
    /// <remarks>The caller supplies the fresh native publication, never an arbitrary historical query.</remarks>
    internal void ObserveSource(CloudMapInput input)
    {
        if (disposed) return;
        if (!input.Storage.IsSupported || !input.Placement.IsFinite
            || allocationSource is { } previous && !SameStorageLifetime(previous, input)
            || program is not null && (!program.IsValid
                || completed is not null && ((IGpuProgram)program).ExecutableRevision != executableRevision))
        { Reset(); return; }
        // Ordinary GPU animation expires contents but retains the compatible allocation.
        if (completed is { } certificate && !certificate.Matches(input)) completed = null;
    }

    /// <summary>Builds every coarse mip after native generation; failure leaves the existing column traversal usable.</summary>
    internal bool TryBuild(CloudMapInput input, Func<bool> current)
    {
        if (disposed) return false;
        ObserveSource(input);
        if (!input.Storage.IsSupported || !input.Placement.IsFinite || !current()) { Reset(); return false; }
        if (failed) return false;
        try
        {
            completed = null;
            RequireNoErrors();
            program ??= createProgram();
            if (!program.EnsureReady()) throw new InvalidOperationException(program.PreparationLog);
            EnsureStorage(input);
            if (!current()) { Reset(); return false; }
            program.Map = map;
            var prepared = program.ProgramLayout.BinaryInterface?.PreparedBindings
                ?? throw new InvalidOperationException("Cloud hierarchy bindings are unavailable.");
            ulong revision = ((IGpuProgram)program).ExecutableRevision;
            if (!EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration("Cloud.Hierarchy"),
                    EngineBoundaryResources.From(prepared), _ => Reduce()))
                throw new InvalidOperationException("Cloud hierarchy compute boundary rejected.");
            RequireNoErrors();
            // Native generation may be revoked during preparation/submission. Never certify an old borrow.
            if (!current() || !program.IsValid || ((IGpuProgram)program).ExecutableRevision != revision)
            { Reset(); return false; }
            executableRevision = revision;
            completed = new(input);
            return true;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            Reset(); failed = true; Failure = error.Message;
            return false;
        }
    }

    /// <summary>Borrows completed storage only against a freshly revalidated native publication.</summary>
    internal bool TryGetCurrent(CloudMapInput current, out DynamicTexture2D? bounds)
    {
        bounds = null;
        if (disposed || program is null) return false;
        if (!program.IsValid || ((IGpuProgram)program).ExecutableRevision != executableRevision)
        { Reset(); return false; }
        if (completed is not { } certificate || !certificate.Matches(current) || texture?.IsValid != true) return false;
        bounds = texture;
        return true;
    }
    #endregion

    #region Resource lifetime
    /// <summary>Withdraws publication and retires owned storage/executable, retaining no permission to sample native data.</summary>
    internal void Reset()
    {
        completed = null; allocationSource = null; Layout = null; executableRevision = 0; failed = false; Failure = null;
        RenderingCleanup.Run(() => { texture?.Dispose(); texture = null; },
            () => { map?.Dispose(); map = null; }, () => { program?.Dispose(); program = null; });
    }

    /// <summary>Closes the lifetime permanently before releasing only VGE-owned resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Reset();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Allocates only coarse mips and a metadata-only native borrower for a supported lifetime.</summary>
    private void EnsureStorage(CloudMapInput input)
    {
        if (texture is not null) return;
        var layout = new CloudHierarchyLayout(input.Storage.Width);
        var limits = GpuSupport.Graphics;
        if (!limits.SupportsArbComputeShader || !limits.SupportsArbShaderImageLoadStore
            || limits.MaxImageUnits < 2 || limits.MaxComputeImageUniforms < 2
            || limits.MaxComputeWorkGroupInvocations < 64 || limits.MaxComputeWorkGroupSize.Length < 2
            || limits.MaxComputeWorkGroupSize[0] < 8 || limits.MaxComputeWorkGroupSize[1] < 8
            || limits.MaxComputeWorkGroupCount.Length < 2
            || (layout.MipWidth(0) + 7) / 8 > limits.MaxComputeWorkGroupCount[0]
            || (layout.MipWidth(0) + 7) / 8 > limits.MaxComputeWorkGroupCount[1]
            || layout.MipWidth(0) > limits.MaxTextureSize)
            throw new InvalidOperationException("Cloud hierarchy extent or compute bindings exceed context limits.");
        map = new BorrowedTexture(input.Storage.Map);
        if (map.Width != layout.NativeWidth || map.Height != layout.NativeWidth
            || map.InternalFormat != PixelInternalFormat.Rgba32f)
            throw new InvalidOperationException("Cloud hierarchy native storage does not match its certificate.");
        texture = DynamicTexture2D.CreateMipmapped(layout.MipWidth(0), layout.MipWidth(0),
            PixelInternalFormat.Rg32f, layout.MipCount, "Cloud.Hierarchy");
        Layout = layout; allocationSource = input;
    }

    /// <summary>Dispatches a disjoint read/write mip pair at each level and establishes final sampler visibility.</summary>
    private void Reduce()
    {
        var layout = Layout!;
        for (int mip = 0; mip < layout.MipCount; mip++)
        {
            int width = layout.MipWidth(mip);
            // The native-leaf dispatch never reads sourceBounds. Bind a different mip even then.
            program!.Source = new(texture!, TextureAccess.ReadOnly, mip == 0 ? 1 : mip - 1);
            program.Target = new(texture!, TextureAccess.WriteOnly, mip);
            program.SetLevel(layout.NativeWidth, width, mip == 0);
            program.Dispatch((width + 7) / 8, (width + 7) / 8);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.TextureFetchBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);
    }

    /// <summary>Separates allocation/executable replacement from ordinary animated content and placement changes.</summary>
    private static bool SameStorageLifetime(CloudMapInput first, CloudMapInput second) =>
        ReferenceEquals(first.Owner, second.Owner) && first.World == second.World && first.Allocation == second.Allocation
        && first.Shader == second.Shader && first.Storage == second.Storage;

    /// <summary>Prevents GL errors, including pre-existing ones, from certifying successful generation.</summary>
    private static void RequireNoErrors()
    {
        ErrorCode first = GL.GetError();
        if (first == ErrorCode.NoError) return;
        while (GL.GetError() != ErrorCode.NoError) { }
        throw new InvalidOperationException("Cloud hierarchy GL boundary error: " + first);
    }
    #endregion
}
