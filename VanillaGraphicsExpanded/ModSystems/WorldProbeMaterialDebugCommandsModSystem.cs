using System;
using System.Numerics;
using System.Text;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class WorldProbeMaterialDebugCommandsModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands
            .GetOrCreate("vge")
            .WithDescription("VanillaGraphicsExpanded debug commands")
            .BeginSubCommand("wp-material")
                .WithDescription("World-probe material lookup diagnostics")
                .BeginSubCommand("stats")
                    .WithDescription("Print latest block-face lookup stats")
                    .HandleWith(_ =>
                    {
                        var stats = PbrMaterialRegistry.Instance.BlockFaceLookupStats;
                        return TextCommandResult.Success(
                            $"Block-face lookup stats: maxBlockId={stats.MaxBlockId}, totalFaces={stats.TotalFaces}, resolvedFaces={stats.ResolvedFaces}, " +
                            $"keyResolutionFailed={stats.TextureKeyResolutionFailed}, surfaceMissing={stats.SurfaceMissingForResolvedKey}, defaultsUsed={stats.DefaultsUsed}");
                    })
                .EndSubCommand()
                .BeginSubCommand("dump")
                    .WithDescription("Dump (blockId,face)->texture->derived terms")
                    .WithArgs(parsers.Int("blockId"))
                    .HandleWith(args =>
                    {
                        int blockId = (int)args[0];
                        return TextCommandResult.Success(Dump(api, blockId));
                    })
                .EndSubCommand()
            .EndSubCommand();
    }

    private static string Dump(ICoreClientAPI api, int blockId)
    {
        Block? block = TryGetBlock(api, blockId);
        if (block is null)
        {
            return $"No block found for id={blockId}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"BlockId={blockId} Code={block.Code}");

        for (byte face = 0; face < 6; face++)
        {
            string faceName = face switch
            {
                0 => "North",
                1 => "East",
                2 => "South",
                3 => "West",
                4 => "Up",
                5 => "Down",
                _ => "?",
            };

            bool hasKey = BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, face, out AssetLocation texKey, out string? fromKey);
            _ = PbrMaterialRegistry.Instance.TryGetDerivedSurface(blockId, face, out DerivedSurface ds);

            sb.Append(faceName);
            sb.Append(": ");

            if (!hasKey)
            {
                sb.Append("tex=<unresolved>");
            }
            else
            {
                sb.Append("tex=");
                sb.Append(texKey);
                if (!string.IsNullOrEmpty(fromKey))
                {
                    sb.Append(" (from '");
                    sb.Append(fromKey);
                    sb.Append("')");
                }
            }

            sb.Append(" | diffuse=");
            AppendVec3(sb, ds.DiffuseAlbedo);
            sb.Append(" f0=");
            AppendVec3(sb, ds.SpecularF0);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static Block? TryGetBlock(ICoreClientAPI api, int blockId)
    {
        if (api.World?.Blocks is null)
        {
            return null;
        }

        if ((uint)blockId < (uint)api.World.Blocks.Count)
        {
            Block? direct = api.World.Blocks[blockId];
            if (direct is not null && direct.BlockId == blockId)
            {
                return direct;
            }
        }

        // Fallback scan (defensive; avoids assumptions about indexing).
        foreach (Block? b in api.World.Blocks)
        {
            if (b is not null && b.BlockId == blockId)
            {
                return b;
            }
        }

        return null;
    }

    private static void AppendVec3(StringBuilder sb, Vector3 v)
    {
        sb.Append('(');
        sb.Append(v.X.ToString("0.###"));
        sb.Append(',');
        sb.Append(v.Y.ToString("0.###"));
        sb.Append(',');
        sb.Append(v.Z.ToString("0.###"));
        sb.Append(')');
    }
}
