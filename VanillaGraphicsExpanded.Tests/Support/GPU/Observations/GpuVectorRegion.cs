using System.Numerics;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Supplies one independent constant tuple and channel mask over a view-relative image region.</summary>
internal readonly record struct GpuVectorRegion(RHITextureRegion Region, Vector4 Expected, uint ChannelMask = 15);
