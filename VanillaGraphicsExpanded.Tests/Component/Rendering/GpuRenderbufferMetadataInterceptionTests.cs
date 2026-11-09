using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks external renderbuffer allocation and manager-owned image dependency publication.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed unsafe class GpuRenderbufferMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    private static int[]? capturedAdvancedArguments;
    private static int renderbufferBinds;
    /// <summary>Matches the renderbuffer selector ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindRenderbufferCall(int target, uint name);
    /// <summary>Matches the optional named coverage allocation ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void CoverageStorageCall(uint name, int coverage, int samples, int format, int width, int height);
    #region Public API



    /// <summary>Checks unsupported extension dispatch through a controlled native substitute, without claiming extension execution.</summary>
    [Fact]
    public void AdvancedExtensionAdapterForwardsExactArguments()
    {
        // Arrange: Substitute unavailable extension entrypoints while retaining their declared native signatures.
        // Act and assert: Invoke the production adapter and verify every argument reaches the original exactly.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var native = new NativeDriverCapture();
        var original = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, void>)native.Original("glNamedRenderbufferStorageMultisample");
        native.Add("glNamedRenderbufferStorageMultisampleCoverageEXT", new CoverageStorageCall((name, coverage, samples, format, width, height) =>
        {
            capturedAdvancedArguments = [unchecked((int)name), coverage, samples, format, width, height];
            // The stand-in supplies ordinary native multisample storage; production observation
            // still runs through the optional entrypoint and its actual post-call ABI.
            original(name, samples, format, width, height);
        }));
        native.Install();
        try
        {
            using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            ulong previous = entry.MetadataRevision;
            capturedAdvancedArguments = null;
            AllocateAdvanced(owner.RenderbufferId);
            Assert.Equal(new[] { owner.RenderbufferId, 4, 4, (int)InternalFormat.Rgba8, 12, 6 }, capturedAdvancedArguments);
            Assert.Equal(12, owner.Width);
            Assert.Equal(6, owner.Height);
            Assert.Equal(previous + 1, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { capturedAdvancedArguments = null; fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }




    #endregion

    #region Private

    /// <summary>Exercises exact extension signatures while their unavailable hardware implementation is substituted.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateAdvanced(int id)
    {
        GL.Ext.NamedRenderbufferStorageMultisampleCoverage(id, 4, 4, InternalFormat.Rgba8, 12, 6);
    }

    /// <summary>Installs the production engine transpiler on this fixture's native call boundary.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.RenderbufferMetadata");

        return harmony;
    }

    /// <summary>Executes ordinary and multisample storage through core and EXT bound/named signatures.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Allocate(int variant, int id, int width)
    {
        switch (variant)
        {
            case 0: GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, 4); break;
            case 1: GL.NamedRenderbufferStorage(id, RenderbufferStorage.Rgba8, width, 4); break;
            case 2: GL.Ext.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, width, 4); break;
            case 3: GL.Ext.NamedRenderbufferStorage(id, RenderbufferStorage.Rgba8, width, 4); break;
            case 4: GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 5: GL.NamedRenderbufferStorageMultisample(id, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 6: GL.Ext.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, 4, RenderbufferStorage.Rgba8, width, 4); break;
            case 7: GL.Ext.NamedRenderbufferStorageMultisample(id, 4, RenderbufferStorage.Rgba8, width, 4); break;
        }
    }

    /// <summary>Injects an unavailable native metadata read without changing actual driver allocation behavior.</summary>
    private sealed class FailedReader(bool throws) : IGpuResourceMetadataReader<GpuRenderbufferMetadata>
    {
        internal int Reads { get; private set; }
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuRenderbufferMetadata metadata)
        {
            Reads++;
            metadata = default;
            if (throws) throw new InvalidOperationException("Injected renderbuffer read failure.");
            return false;
        }
    }
    #endregion
}
