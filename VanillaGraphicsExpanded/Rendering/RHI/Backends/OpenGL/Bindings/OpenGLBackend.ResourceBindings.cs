using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes one fully validated captured resource set using existing cache and uniform allocation authority.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly List<GpuResource> executionResources = new();
    private readonly HashSet<GpuResource> executionResourceSet = new(ReferenceEqualityComparer.Instance);
    private readonly ConditionalWeakTable<RHIResourceBindings, OpenGLBindingPlan> bindingPlans = new();
    private OpenGLBindingPlan? executionBindingPlan;
#if DEBUG
    /// <summary>Counts immutable input-plan construction independently of execution checks.</summary>
    internal long BindingPlansPrepared { get; private set; }
#endif

    #region Public API
    /// <inheritdoc />
    public void BindResources(RHIShaderRef reference, RHIResourceBindings inputs)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Shader != reference) throw new InvalidOperationException("Captured bindings belong to a different executable generation.");
        var shader = reference.RequireShader();
        BeginBindingExecution(shader, inputs);
        try { PublishResourceBindings(shader, reuseCommittedUniforms: false); }
        finally { ReleaseExecutionResources(); }
    }
    #endregion

    #region Private
    /// <summary>Retains and validates the complete set for the entire binding or rendering command.</summary>
    private void BeginBindingExecution(RHIShader shader, RHIResourceBindings? inputs)
    {
        if (executionResources.Count != 0) throw new InvalidOperationException("Resource execution cannot recurse.");
        try
        {
            RetainExecutionResource(shader.Program);
            if (inputs is null)
            {
                if (shader.Interface.PreparedBindings.ActiveEntries.Count != 0)
                    throw new InvalidOperationException("Rendering requires a captured complete resource table.");
                return;
            }
            if (inputs.Shader != (RHIShaderRef)shader)
                throw new InvalidOperationException("Captured resources belong to a different executable generation.");
            if (!bindingPlans.TryGetValue(inputs, out var plan))
            {
                plan = new OpenGLBindingPlan(shader.Interface.PreparedBindings, inputs);
                bindingPlans.Add(inputs, plan);
#if DEBUG
                BindingPlansPrepared++;
#endif
            }
            if (!ReferenceEquals(plan.Interface, shader.Interface.PreparedBindings))
                throw new InvalidOperationException("Prepared binding interface changed.");
            executionBindingPlan = plan;
            ValidateResourceBindings(plan);
        }
        catch { ReleaseExecutionResources(); throw; }
    }
    /// <summary>Releases the same original wrappers after native callbacks and operation cleanup finish.</summary>
    private void ReleaseExecutionResources()
    {
        for (int index = executionResources.Count - 1; index >= 0; index--) executionResources[index].ReleasePassReference();
        executionResources.Clear();
        executionResourceSet.Clear();
        executionBindingPlan = null;
    }
    /// <summary>Applies executed resources, reusing committed uniform ranges only for a rendering command's state restoration.</summary>
    private void PublishResourceBindings(RHIShader shader, bool reuseCommittedUniforms)
    {
        ApplyExecutable(shader);
        var plan = executionBindingPlan ?? throw new InvalidOperationException("Binding execution was not prepared.");
        if (plan.Published && !plan.InputsChanged && plan.PublicationRevision == cache.BindingPublicationRevision
            && ReuseUniformPublication(plan, reuseCommittedUniforms))
        {
            shader.RequireUsable();
            return;
        }
        // Do not certify a partially published set or one interrupted by an external callback.
        plan.Published = false;
        ulong observations = cache.BindingObservationRevision;
        PublishTextureInputs(plan);
        foreach (var input in plan.Other)
        {
            var binding = input.Binding;
            int slot = binding.Contract.Binding.Slot;
            switch (binding.Contract.Kind)
            {
                case ShaderBindingKind.Image:
                    OpenGLImageSlotBindings.BindImageTexture(cache, slot, input.Image);
                    break;
                case ShaderBindingKind.UniformBlock:
                    if (input.HasUniform) input.CommittedUniform = PublishUniformSnapshot(slot, input.Uniform, reuseCommittedUniforms);
                    else PublishBufferInput(binding.Contract.Kind, slot, input.Buffer);
                    break;
                case ShaderBindingKind.StorageBlock:
                case ShaderBindingKind.AtomicCounter:
                    PublishBufferInput(binding.Contract.Kind, slot, input.Buffer);
                    break;
            }
        }
        shader.RequireUsable();
        plan.InputsChanged = false;
        plan.PublicationRevision = cache.BindingPublicationRevision;
        plan.Published = observations == cache.BindingObservationRevision;
    }
    /// <summary>Marks only active captured blocks consumed before dispatch, preserving pending recordings and inactive inputs.</summary>
    private void MarkBindingExecution()
    {
        if (executionBindingPlan is not { } plan) return;
        foreach (var input in plan.Uniforms)
            if (input.Uniform is { } snapshot)
                snapshot.Publication.MarkExecution(snapshot.ContentRevision);
    }

    /// <summary>Retains each original backing wrapper once until native callbacks and publication cleanup return.</summary>
    private void RetainExecutionResource(GpuResource? resource)
    {
        if (resource is null || executionResourceSet.Contains(resource)) return;
        resource.RetainPassReference();
        executionResources.Add(resource);
        executionResourceSet.Add(resource);
        if (resource is GpuTextureView { Source: { } source }) RetainExecutionResource(source);
    }
    #endregion
}
