using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Initializes private native recipients for interception and lifecycle fixtures, independently of portable shader/draw APIs.</summary>
internal static class NativeResourceSetup
{
    #region Internal API
    /// <summary>Establishes an empty realized array for tests that deliberately configure native vertex state.</summary>
    internal static GpuVao CreateVertexArray(string? label = null)
    {
        var owner = new GpuVao();
        try
        {
            if (!GpuResourceManager.CreateVertexArray(new(), owner, out _).HasValue)
                throw new InvalidOperationException("Fixture vertex-array initialization failed.");
            owner.SetDebugName(label);
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }

    /// <summary>Establishes an unlinked program for tests of explicit native linking, observation and retirement.</summary>
    internal static GpuProgramObject CreateProgram(string? label = null)
    {
        var owner = new GpuProgramObject();
        try
        {
            if (!GpuResourceManager.CreateProgram(new(), owner, out _).HasValue)
                throw new InvalidOperationException("Fixture program initialization failed.");
            owner.SetDebugName(label);
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }

    /// <summary>Establishes a pipeline solely for native interception fixtures; production exposes no alternative factory.</summary>
    internal static GpuProgramPipeline CreateProgramPipeline(string? label = null)
    {
        var owner = new GpuProgramPipeline();
        try
        {
            if (!GpuResourceManager.CreateProgramPipeline(new(), owner, out _).HasValue)
                throw new InvalidOperationException("Fixture pipeline initialization failed.");
            owner.SetDebugName(label);
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
}
