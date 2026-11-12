using System.Collections.Immutable;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>Complete early conservative surface bounds, scoped to the exact source/scene interval.</summary>
internal sealed record ShadowSurfacePublication(long Frame,long World,int Dimension,ShadowSurfaceCategory CompleteCategories,
    ImmutableArray<ShadowReceiverRegion> Regions,ImmutableArray<ShaderProgramBase> Programs);

