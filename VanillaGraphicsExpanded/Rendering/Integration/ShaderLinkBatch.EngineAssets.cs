using System.Collections.Generic;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;
namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;
/// <summary>Adapts existing engine callers to the engine-independent batch source boundary.</summary>
internal sealed partial class ShaderLinkBatch
{
    /// <summary>Captures engine asset identity without duplicating digest or import services.</summary>
    internal ShaderLinkBatch(IAssetManager assets, string domain, IEnumerable<ShaderSettings> settings,
        int maximumOutstanding = 8, CancellationToken cancellation = default, bool disable = false)
        : this(Integration.EngineShaderAssets.Create(assets, domain), settings, maximumOutstanding, cancellation, disable) { }
}
