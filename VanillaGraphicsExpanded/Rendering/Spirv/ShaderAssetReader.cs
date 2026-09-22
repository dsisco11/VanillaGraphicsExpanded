using System;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Provides a borrowed, read-only view of a shader asset for synchronous consumption.</summary>
/// <remarks>
/// The backing storage must remain valid and unchanged until the next reader invocation or until
/// the loading operation returns, whichever occurs first. The loader consumes each view before
/// requesting another asset and never retains a view after loading completes.
/// </remarks>
internal delegate ReadOnlySpan<byte> ShaderAssetReader(string path);
