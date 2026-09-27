using System;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Loads runtime terrain stage assets through the shared GLSL import resolver.</summary>
internal static class TerrainTessellationAssets
{
    #region Asset loading
    /// <summary>Loads a fresh pair during shader preparation so asset reloads also refresh stage bodies.</summary>
    internal static TerrainTessellationStages.Sources Load(IAssetManager assets)
    {
        var preprocessor = new ShaderSyntaxTreePreprocessor(new AssetSyntaxTreeResourceResolver(assets, Constants.ModId));
        return new(LoadStage("tcsh"), LoadStage("tesh"));

        /// <summary>Expands imports without evaluating the engine's later compilation defines.</summary>
        string LoadStage(string extension)
        {
            string path = $"shaders/includes/tessellation/terrain.{extension}";
            var location = AssetLocation.Create(path, Constants.ModId);
            string source = assets.TryGet(location, loadAsset: true)?.ToText()
                ?? throw new InvalidOperationException($"Missing terrain tessellation asset: {location}");
            var result = preprocessor.Process(new ResourceId($"{Constants.ModId}:{path}"),
                SyntaxTree.Parse(source, GlslSchema.Instance));
            if (!result.Success) throw new InvalidOperationException($"Cannot expand terrain tessellation asset: {location}: {string.Join(", ", result.Diagnostics)}");
            return result.Content.ToText();
        }
    }
    #endregion
}
