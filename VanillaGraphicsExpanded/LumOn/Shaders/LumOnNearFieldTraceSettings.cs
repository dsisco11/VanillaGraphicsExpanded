using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Shaders;

/// <summary>
/// Complete traversal policy for a published near-field scene. Mapping and consumer domains remain scene-owned.
/// When supplied explicitly, null origin bounds remove the additional launch-origin restriction and zero reach
/// disables the additional reach limit. Omitting this policy at binding retains the scene-derived runtime defaults.
/// </summary>
/// <param name="MaxSteps">Maximum voxel traversal steps per ray.</param>
/// <param name="SupportedOrigins">Optional world-coordinate bounds restricting ray launch positions.</param>
/// <param name="MaximumTraceReach">Additional reach bound; zero leaves it unrestricted.</param>
internal sealed record LumOnNearFieldTraceSettings(int MaxSteps = 256,
    PartitionBounds? SupportedOrigins = null, float MaximumTraceReach = 0);
