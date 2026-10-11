using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Locates explicitly composed registries for existing engine-facing callers.</summary>
internal static class GpuShaderProgramLibraries
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, GpuShaderPrograms> libraries = new();
    #region Public API
    #region Composition
    /// <summary>Creates one client rendering registry; late access never performs this operation implicitly.</summary>
    internal static GpuShaderPrograms Create(ICoreClientAPI api)
    {
        if (libraries.TryGetValue(api, out var existing)) return existing.IsClosed
            ? throw new ObjectDisposedException(nameof(GpuShaderPrograms)) : existing;
        var registry = new GpuShaderPrograms(domain => Integration.EngineShaderAssets.Create(api.Assets, domain, api.Logger));
        libraries.Add(api, registry);
        Integration.ClientRenderingLifetime.Create(api, registry);
        return registry;
    }
    /// <summary>Requires the composition root to have established the rendering lifetime.</summary>
    internal static GpuShaderPrograms Require(ICoreClientAPI api) => libraries.TryGetValue(api, out var registry)
        ? registry : throw new InvalidOperationException("VGE shader registry has not been composed.");
    #endregion
    #region Declarations
    /// <summary>Declares an immutable descriptor before evaluating an owner factory.</summary>
    internal static T Declare<T>(ICoreClientAPI api, string name, Func<T> factory, ShaderSettings? settings = null,
        string domain = PBR.ShaderImportsSystem.DefaultDomain) where T : GpuProgram
    {
        settings ??= new ShaderSettings(GpuShaderContracts.Registry.FindProgram(name));
        return Require(api).Declare(new(domain, name, typeof(T), settings), factory);
    }
    /// <summary>Adapts optional typed lookup to the existing rendering registry.</summary>
    internal static T? Get<T>(ICoreClientAPI api, string name) where T : GpuProgram => Require(api).Get<T>(name);
    /// <summary>Captures existing declarations for explicit configuration and preload.</summary>
    internal static ImmutableArray<GpuProgram> GetAll(ICoreClientAPI api) => Require(api).GetAll();
    /// <summary>Removes an owner through its registry lifetime.</summary>
    internal static void Remove(ICoreClientAPI api, string name) => Require(api).Remove(name);
    #endregion
    #region Preparation and retirement
    /// <summary>Preloads only the selected configured declarations.</summary>
    internal static bool Preload(ICoreClientAPI api, ImmutableArray<GpuProgram> selection) => Require(api).Preload(selection);
    /// <summary>Closes a composed registry and retains its tombstone for late callbacks.</summary>
    internal static void Dispose(ICoreClientAPI api) { if (libraries.TryGetValue(api, out var registry)) registry.Dispose(); }
    #endregion
    #endregion
}
