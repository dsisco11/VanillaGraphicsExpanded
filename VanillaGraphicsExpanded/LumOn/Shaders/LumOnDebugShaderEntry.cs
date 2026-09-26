using System;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the deferred creation, reload and failed-attempt state of one debug program.</summary>
internal sealed class LumOnDebugShaderEntry : IDisposable
{
    internal LumOnDebugShaderProgram Program { get; }
    private bool registered;
    private bool reloadRequired = true;
    private ShaderLoadPlan? failedPlan;
    private ShaderSettings? readySettings;

    #region Creation and readiness
    /// <summary>Creates a settings owner without reading binaries or allocating shader executables.</summary>
    internal LumOnDebugShaderEntry(ICoreClientAPI api, string name)
    {
        Program = new LumOnDebugShaderProgram
        {
            PassName = name,
            AssetDomain = ShaderImportsSystem.DefaultDomain,
            DeferCompilationUntilSelected = true
        };
        Program.Initialize(api);
    }

    /// <summary>Marks changed assets for replacement on the next selection while retaining current settings.</summary>
    internal void Invalidate()
    {
        reloadRequired = true;
        failedPlan = null;
        // The engine removes registered owners during its reload. Unregistered declarations survive it.
        if (Program.Disposed) registered = false;
    }

    /// <summary>Links only when selected, publishes successful generations and suppresses repeated failing frame attempts.</summary>
    internal bool EnsureReady(ICoreClientAPI api)
    {
        // Selection may occur after engine teardown but before its queued family invalidation callback.
        if (Program.Disposed) registered = false;
        var settings = Program.RequestedSettings;
        if (!reloadRequired && !Program.Disposed &&
            (ReferenceEquals(readySettings, settings) || !Program.NeedsRecompile))
        {
            readySettings = settings;
            return true;
        }
        var requested = new ShaderLoadPlan(settings);
        if (failedPlan?.SameInputs(requested) == true) return false;
        if (!Program.CompileAndLink())
        {
            // A changed selection or asset reload permits another attempt. The installed owner remains intact.
            failedPlan = requested;
            return false;
        }
        if (!registered)
        {
            api.Shader.RegisterMemoryShaderProgram(Program.PassName, Program);
            registered = true;
        }
        reloadRequired = false;
        failedPlan = null;
        readySettings = Program.InstalledSettings;
        return true;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases both registered executables and never-linked declarations at application teardown.</summary>
    public void Dispose()
    {
        // Engine disposal detaches non-null stages even if their IDs are zero. Empty declarations own no GL stages.
        if (Program.ProgramId == 0)
        {
            Program.VertexShader = null;
            Program.FragmentShader = null;
            Program.GeometryShader = null;
        }
        Program.Dispose();
    }
    #endregion
}
