using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Sequences preparation, publication and retirement for VGE executable owners.</summary>
internal interface IGpuProgram : IShaderSubmissionTarget, IDisposable
{
    #region Owner contract
    /// <summary>Provides stable lifecycle data without duplicating native ownership.</summary>
    GpuProgramLifetime Lifetime { get; }
    /// <summary>Prepares the family-specific executable and reports readiness.</summary>
    bool PrepareExecutable();
    /// <summary>Establishes the family's existing activation ownership before publication.</summary>
    void BindExecutable();
    /// <summary>Publishes retained inputs through the authored or generated Submit override.</summary>
    void PublishInputs();
    /// <summary>Clears this family's failed activation.</summary>
    void ClearActivation(bool bindingEntered);
    /// <summary>Captures the existing family restoration scope before activation.</summary>
    IDisposable OpenUseScope();
    /// <summary>Releases the family's singular native executable owner.</summary>
    void ReleaseExecutable();
    /// <summary>Releases additional family-owned resources on terminal retirement.</summary>
    void ReleaseResources();
    #endregion

    #region Default workflows
    /// <summary>Checks lifecycle admission before family-specific demand preparation.</summary>
    bool Prepare()
    {
        Lifetime.RequireOutsidePublication();
        if (Lifetime.IsRetired) return false;
        return PrepareExecutable();
    }

    /// <summary>Publishes all desired inputs even when the executable is already bound.</summary>
    void Activate()
    {
        // Reject recursion before this call owns cleanup of an activation.
        Lifetime.RequireMutation();
        bool bindingEntered = false;
        try
        {
            if (!Prepare()) throw new InvalidOperationException("GPU program preparation failed.");
            // Family cleanup may withdraw an already-active failed graphics generation, but
            // a new compute owner cannot clear an unrelated binding before entering its bind.
            bindingEntered = true;
            BindExecutable();
            Lifetime.IsPublishing = true;
            try { PublishInputs(); }
            finally { Lifetime.IsPublishing = false; }
        }
        catch (Exception failure)
        {
            try { ClearActivation(bindingEntered); }
            catch (Exception cleanup) { throw new AggregateException(failure, cleanup); }
            throw;
        }
    }

    /// <summary>Enters the established family scope after rejecting recursive or retired use.</summary>
    IDisposable BeginUse()
    {
        Lifetime.RequireMutation();
        var scope = OpenUseScope();
        try { Activate(); return scope; }
        catch (Exception activation)
        {
            // A failed publication still owes the enclosing scope its restoration.
            try { scope.Dispose(); }
            catch (Exception restoration) { throw new AggregateException(activation, restoration); }
            throw;
        }
    }

    /// <summary>Marks terminal lifetime once and attempts every independent cleanup operation.</summary>
    void Retire()
    {
        if (Lifetime.IsRetired) return;
        Lifetime.RequireMutation();
        Lifetime.IsRetired = true;
        List<Exception>? failures = null;
        // Terminal admission closes before any cleanup can invoke owner code again.
        foreach (var buffer in Lifetime.OwnedUniforms)
            try { buffer.Dispose(); }
            catch (Exception failure) { (failures ??= new()).Add(failure); }
        Lifetime.OwnedUniforms.Clear();
        try { ReleaseResources(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        try { ReleaseExecutable(); }
        catch (Exception failure) { (failures ??= new()).Add(failure); }
        if (failures != null) throw new AggregateException(failures);
    }

    /// <summary>Registers an owned CPU block and enforces the same input mutation contract.</summary>
    T RegisterUniform<T>(T buffer) where T : CpuUniformBuffer
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Lifetime.RequireMutation();
        if (!Lifetime.OwnedUniforms.Contains(buffer)) Lifetime.OwnedUniforms.Add(buffer);
        buffer.SetWriteGuard(Lifetime.RequireMutation);
        return buffer;
    }
    #endregion
}
