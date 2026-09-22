using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Routes generated option accessors through existing define ownership and reload scheduling.</summary>
public abstract partial class GpuProgram
{
    #region Typed option access
    /// <summary>Reads the current normalized selection, including a declared default when no override exists.</summary>
    internal T GetShaderOption<T>(ShaderOption<T> option) where T : struct
    {
        lock (defineLock) return ShaderOptionAccess.Get(new ShaderSettings(ProgramContract, defines), option);
    }

    /// <summary>Validates before publishing an override and schedules through the existing recompile path.</summary>
    internal void SetShaderOption<T>(ShaderOption<T> option, T value) where T : struct
    {
        bool changed;
        lock (defineLock)
        {
            var prior = new ShaderSettings(ProgramContract, defines);
            var selected = prior.With(option, value);
            changed = prior.Values[option.Name] != selected.Values[option.Name];
            if (changed)
            {
                // Store one canonical spelling so old string-based loaders see the same selection.
                foreach (string alias in option.Aliases) defines.Remove(alias);
                defines[option.Name] = selected.Values[option.Name].Canonical;
            }
        }
        if (changed) RequestRecompile();
    }
    #endregion
}
