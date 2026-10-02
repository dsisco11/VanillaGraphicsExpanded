using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains successful ordinary-value publication for the selected executable generation.</summary>
internal struct ShaderUniformPublication<T>
{
    private int location;
    private T successful;
    private bool hasSuccess;
    private object? executableIdentity;

    /// <summary>Counts actual successful uploads independently of desired assignments and resource binds.</summary>
    internal int Uploads { get; private set; }

    #region Public API
    /// <summary>Checks retained numeric type and array extent without publishing or losing pending values.</summary>
    internal void Validate(IShaderSubmissionTarget owner, int declaredLocation, T value)
    {
        var prepared = owner.ProgramLayout.BinaryInterface?.PreparedBindings ??
            throw new InvalidOperationException("Ordinary uniform publication requires a prepared executable.");
        if (!prepared.ContainsUniformLocation(declaredLocation)) return;
        if (prepared.UniformType(declaredLocation) != ShaderUniformUpload.Type(value))
            throw new InvalidOperationException("Ordinary uniform input does not match its linked upload type.");
        if (value is Array array && (array.Length == 0 || array.Length > prepared.UniformArrayLength(declaredLocation)))
            throw new InvalidOperationException("Ordinary uniform input exceeds the linked array extent.");
    }

    /// <summary>Uploads first use and changed values, committing history only after a successful write.</summary>
    internal void Publish(IShaderSubmissionTarget owner, int declaredLocation, T value)
    {
        var current = owner.ProgramLayout.BinaryInterface ?? throw new InvalidOperationException("No prepared executable.");
        if (!current.PreparedBindings.ContainsUniformLocation(declaredLocation)) return;
        if (!GlStateCache.Current.TryGetCachedCurrentProgram(out int bound) || bound != owner.ProgramId)
            throw new InvalidOperationException("Ordinary uniforms can only publish to the active executable.");
        Publish(current, declaredLocation, value, ShaderUniformUpload.Write);
    }

    /// <summary>Uses the same transactional history with an injectable writer for CPU failure/retry verification.</summary>
    internal void Publish(object generation, int declaredLocation, T value, Action<int, T> writer)
    {
        // Desired inputs outlive executable replacement; successful history never does.
        if (hasSuccess && ReferenceEquals(executableIdentity, generation) &&
            location == declaredLocation && ShaderUniformValue.Equal(successful, value)) return;
        writer(declaredLocation, value);
        successful = ShaderUniformValue.Snapshot(value);
        executableIdentity = generation;
        location = declaredLocation;
        hasSuccess = true;
        Uploads++;
    }
    #endregion

}
