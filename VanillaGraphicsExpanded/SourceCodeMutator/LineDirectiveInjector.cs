using System;
using System.Collections.Generic;
using System.Linq;

using TinyPreprocessor.Core;
using TinyPreprocessor.SourceMaps;
using TinyPreprocessor.Text;

using TinyAst.Preprocessor.Bridge.Content;

using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

internal static class LineDirectiveInjector
{
    internal sealed class Result
    {
        public required bool Success { get; init; }
        public required SyntaxTree OutputTree { get; init; }
        public required IReadOnlyDictionary<int, string> SourceIdToResource { get; init; }

        public static Result Noop(SyntaxTree tree) => new()
        {
            Success = false,
            OutputTree = tree,
            SourceIdToResource = new Dictionary<int, string>()
        };
    }

    public static Result TryInject(
        SyntaxTree tree,
        SourceMap sourceMap,
        System.Func<ResourceId, SyntaxTree> contentProvider)
    {
        ArgumentNullException.ThrowIfNull(tree);

        ArgumentNullException.ThrowIfNull(sourceMap);
        ArgumentNullException.ThrowIfNull(contentProvider);

        // Ask the SourceMap for a complete decomposition of the generated output.
        // This returns source ranges which can cross multiple resources; we inject at each generated range start.
        var ranges = sourceMap.QueryRangeByEnd(0, tree.TextLength);
        if (ranges.Count == 0)
        {
            return Result.Noop(tree);
        }

        // Never inject before #version.
        int minInsertOffset = GetMinInsertOffset(tree);

        // Stable numeric source ids per unique ResourceId.
        var resourceToId = new Dictionary<ResourceId, int>();
        var idToResource = new Dictionary<int, string>();

        int NextId(ResourceId resource)
        {
            if (!resourceToId.TryGetValue(resource, out int id))
            {
                id = resourceToId.Count + 1;
                resourceToId[resource] = id;
                idToResource[id] = resource.Path;
            }
            return id;
        }

        // Prefer the canonical provider in case the resolver implementation changes across package versions.
        // Fallback to direct instantiation if the provider can't supply it for any reason.
        if (!SyntaxTreeContentBoundaryResolverProvider.Instance.TryGet<SyntaxTree, LineBoundary>(out var boundaryResolver))
        {
            boundaryResolver = new SyntaxTreeLineBoundaryResolver();
        }

        // Build a list of insertion operations as (generatedOffset, directiveText).
        var inserts = new List<(int offset, string directive)>(capacity: Math.Min(512, ranges.Count));
        foreach (var range in ranges)
        {
            int offset = Math.Max(minInsertOffset, range.GeneratedStartOffset);

            if (offset < 0 || offset >= tree.TextLength)
            {
                continue;
            }

            // If we had to clamp to a later position (e.g. after #version) and that falls outside this segment,
            // skip this segment and let the later segment(s) contribute an insertion point.
            if (offset >= range.GeneratedEndOffset)
            {
                continue;
            }

            // Prefer the SourceMap's point query (it can be more precise than range endpoints).
            // Fall back to range-derived offset if needed.
            var loc = sourceMap.Query(offset);

            ResourceId resource = loc?.Resource ?? range.Resource;

            int segmentDelta = offset - range.GeneratedStartOffset;
            int originalOffset = loc?.OriginalOffset ?? (range.OriginalStartOffset + segmentDelta);

            int sourceId = NextId(resource);

            var content = contentProvider(resource);
            int originalLineOneBased = GetOriginalLineOneBased(content, resource, originalOffset, boundaryResolver);

            inserts.Add((offset, $"#line {originalLineOneBased} {sourceId}\n"));
        }

        if (inserts.Count == 0)
        {
            return Result.Noop(tree);
        }

        // Deduplicate by offset: last wins (they should be identical for same segment start).
        var insertsByOffset = inserts
            .GroupBy(i => i.offset)
            .Select(g => g.Last())
            .OrderBy(i => i.offset)
            .ToArray();

        var editor = tree.CreateEditor();
        foreach (var ins in insertsByOffset)
        {
            // Insert at the node that contains this position. This is the most stable anchor for SyntaxEditor.
            // NOTE: if this lands in the middle of a leaf, FindLeafAt returns that leaf and we insert before it.
            // That is acceptable for #line directives (they are line-based and should begin at boundaries).
            var leaf = tree.FindLeafAt(ins.offset);
            if (leaf is null)
            {
                continue;
            }

            editor.InsertBefore(leaf, ins.directive);
        }

        editor.Commit();

        return new Result
        {
            Success = true,
            OutputTree = tree,
            SourceIdToResource = idToResource
        };
    }

    private static int GetOriginalLineOneBased(
        SyntaxTree content,
        ResourceId resource,
        int originalOffset,
        IContentBoundaryResolver<SyntaxTree, LineBoundary> boundaryResolver)
    {
        if (content.TextLength <= 0)
        {
            return 1;
        }

        int safeOffset = Math.Clamp(originalOffset, 0, Math.Max(0, content.TextLength - 1));
        int safeEndExclusive = Math.Min(safeOffset + 1, content.TextLength);

        var range = safeOffset..safeEndExclusive;
        if (SyntaxTreeLineColumnMapper.TryGetLineColumnRange(
                content,
                resource,
                range,
                boundaryResolver,
                out var start,
                out _))
        {
            return Math.Max(1, start.Line);
        }

        return 1;
    }

    private static int GetMinInsertOffset(SyntaxTree tree)
    {
        // #version must be the first directive in GLSL.
        // We clamp injection offsets to be at/after the #version line.
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        var version = tree.Select(versionQuery).FirstOrDefault();

        if (version is null)
        {
            return 0;
        }

        int start = Math.Max(0, version.Position);
        if (tree.TextLength <= 0)
        {
            return 0;
        }

        // Ensure we don't insert before the newline that terminates the #version directive,
        // otherwise the line resolver will correctly report line 1.
        // We want "#line 2 ..." to apply to subsequent lines.
        try
        {
            string text = tree.ToText();
            int searchStart = Math.Clamp(start, 0, Math.Max(0, text.Length - 1));
            int newline = text.IndexOf('\n', searchStart);
            if (newline >= 0)
            {
                int afterNewline = newline + 1;
                return Math.Clamp(afterNewline, 0, Math.Max(0, tree.TextLength - 1));
            }
        }
        catch
        {
        }

        return Math.Clamp(start, 0, Math.Max(0, tree.TextLength - 1));
    }
}
