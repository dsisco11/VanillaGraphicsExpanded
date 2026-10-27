using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Requests target-specific native query realization for private creation.</summary>
internal readonly record struct GpuQueryInitialization(QueryKind Kind);
