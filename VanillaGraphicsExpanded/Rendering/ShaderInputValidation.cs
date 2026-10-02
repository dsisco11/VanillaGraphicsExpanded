using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains preparation selection and successful compatibility checks for one shader-owned input.</summary>
internal struct ShaderInputValidation
{
    private GpuPreparedBindings? table;
    private ulong identity;
    private GpuPreparedBindings.Entry selected;
    private GpuPreparedBindings.Entry validated;
    private InputKey key;
    private bool hasValidation;
    /// <summary>Counts successful compatibility checks, excluding lifetime checks and context cache comparisons.</summary>
    internal int CompatibilityChecks { get; private set; }
    /// <summary>Counts entry resolutions on installed executable changes rather than each use.</summary>
    internal int EntryResolutions { get; private set; }

    /// <summary>Captures the resource facts on which compatibility depends, independently of wrapper equality.</summary>
    internal readonly record struct InputKey(object Resource, nint Handle, int Target, int Format,
        int Width, int Height, int Depth, int Mips, GpuTextureBinding View, nint Offset, nint Size, nint Capacity);

    #region Public API
    /// <summary>Resolves the input once per prepared executable generation without global binding state.</summary>
    internal GpuPreparedBindings.Entry Resolve(IShaderSubmissionTarget owner, ulong inputIdentity)
    {
        var current = owner.ProgramLayout.BinaryInterface?.PreparedBindings ??
            throw new System.InvalidOperationException("Shader has no prepared executable interface.");
        if (!ReferenceEquals(table, current) || identity != inputIdentity)
        {
            table = current;
            identity = inputIdentity;
            selected = current.Resolve(inputIdentity);
            hasValidation = false;
            EntryResolutions++;
        }
        return selected;
    }

    /// <summary>Checks the last successful input facts without repeating format or view validation.</summary>
    internal readonly bool Matches(GpuPreparedBindings.Entry binding, InputKey input) =>
        hasValidation && validated == binding && key == input;

    /// <summary>Records compatibility only after validation succeeds, preserving failed-input retries.</summary>
    internal void Commit(GpuPreparedBindings.Entry binding, InputKey input)
    {
        validated = binding;
        key = input;
        hasValidation = true;
        CompatibilityChecks++;
    }

    /// <summary>Snapshots allocation metadata already owned by the texture without driver queries.</summary>
    internal static InputKey TextureKey(GpuTexture texture, GpuTextureBinding view = default) => new(texture,
        texture.TextureId, (int)texture.TextureTarget, (int)texture.InternalFormat, texture.Width, texture.Height,
        texture.Depth, texture.StorageMipLevels, view, 0, 0, 0);

    /// <summary>Snapshots the retained range and current owner-controlled capacity.</summary>
    internal static InputKey StorageKey(GpuStorageBufferBinding range) => new(range.Buffer!,
        range.Buffer!.BufferId, 0, 0, 0, 0, 0, 0, default, range.OffsetBytes, range.SizeBytes, range.Buffer.SizeBytes);
    #endregion
}
