using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace VanillaGraphicsExpanded.DebugView;

public static partial class VgeBuiltInDebugViews
{
    private static DebugViewDefinition CreateGpuProfilerView()
        => new(
            id: GpuProfilerViewId,
            name: "GPU Profiler",
            category: CategoryProfiling,
            description: "View GlGpuProfiler snapshot (with prefix filter and sort).",
            registerRenderer: _ =>
            {
                GlGpuProfiler.Instance.Enabled = true;
                return new ActionDisposable(() => GlGpuProfiler.Instance.Enabled = false);
            },
            activationMode: DebugViewActivationMode.Toggle,
            createPanel: ctx => new GpuProfilerPanel(viewId: GpuProfilerViewId, ctx.Capi));

    private sealed class GpuProfilerPanel : DebugViewPanelBase
    {
        private readonly string viewId;
        private readonly ICoreClientAPI capi;

        private readonly string[] enabledValues = ["On", "Off"];
        private readonly string[] enabledNames = ["On", "Off"];

        private readonly string[] categoryValues = ["All", "PBR", "LumOn", "Debug"];
        private readonly string[] categoryNames = ["All", "PBR", "LumOn", "Debug"];

        private readonly string[] sortValues = ["Name", "LastMs", "AvgMs", "MaxMs"];
        private readonly string[] sortNames = ["Name", "Last", "Avg", "Max"];

        private GuiComposer? composer;
        private string? keyPrefix;

        private string? lastDumpPath;
        private string? lastDumpError;

        public GpuProfilerPanel(string viewId, ICoreClientAPI capi)
        {
            this.viewId = viewId;
            this.capi = capi;
        }

        public override bool WantsGameTick => true;

        public override void Compose(GuiComposer composer, ElementBounds bounds, string keyPrefix)
        {
            this.composer = composer;
            this.keyPrefix = keyPrefix;

            const double rowH = 30;
            const double labelW = 120;
            const double gap = 10;
            double boundsW = bounds.fixedWidth > 0 ? bounds.fixedWidth : bounds.OuterWidth;
            double boundsH = bounds.fixedHeight > 0 ? bounds.fixedHeight : bounds.OuterHeight;
            double controlW = Math.Min(280, Math.Max(200, boundsW - labelW - gap));

            var fontLabel = CairoFont.WhiteDetailText();
            var fontSmall = CairoFont.WhiteSmallText();

            ElementBounds labelEnabled = ElementBounds.Fixed(0, 0, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlEnabled = ElementBounds.Fixed(labelW + gap, 0, controlW, rowH).WithParent(bounds);

            ElementBounds labelCat = ElementBounds.Fixed(0, rowH + 8, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlCat = ElementBounds.Fixed(labelW + gap, rowH + 8, controlW, rowH).WithParent(bounds);

            ElementBounds labelSort = ElementBounds.Fixed(0, (rowH + 8) * 2, labelW, rowH).WithParent(bounds);
            ElementBounds ctrlSort = ElementBounds.Fixed(labelW + gap, (rowH + 8) * 2, controlW, rowH).WithParent(bounds);

            ElementBounds dumpBtnBounds = ElementBounds.Fixed(0, (rowH + 8) * 3, Math.Min(boundsW, labelW + gap + controlW), rowH).WithParent(bounds);

            ElementBounds textBounds = ElementBounds.Fixed(
                0,
                (rowH + 8) * 4 + 8,
                boundsW,
                Math.Max(60, boundsH - ((rowH + 8) * 4 + 8)))
                .WithParent(bounds);

            int enabledIndex = DebugViewController.Instance.IsActive(viewId) ? 0 : 1;

            composer
                .AddStaticText("Enabled", fontLabel, labelEnabled)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        enabledValues,
                        enabledNames,
                        enabledIndex,
                        OnEnabledChanged,
                        ctrlEnabled,
                        fontSmall),
                    $"{keyPrefix}-enabled")

                .AddStaticText("Category", fontLabel, labelCat)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        categoryValues,
                        categoryNames,
                        0,
                        OnSelectionChanged,
                        ctrlCat,
                        fontSmall),
                    $"{keyPrefix}-cat")

                .AddStaticText("Sort", fontLabel, labelSort)
                .AddInteractiveElement(
                    new GuiElementDropDownCycleOnArrow(
                        capi,
                        sortValues,
                        sortNames,
                        1,
                        OnSelectionChanged,
                        ctrlSort,
                        fontSmall),
                    $"{keyPrefix}-sort")

                .AddSmallButton(
                    text: "Dump JSON snapshot",
                    onClick: OnDumpJsonClicked,
                    bounds: dumpBtnBounds,
                    style: EnumButtonStyle.Normal,
                    key: $"{keyPrefix}-dumpjson")

                .AddDynamicText("(Profiler will populate after a few frames.)", fontSmall, textBounds, $"{keyPrefix}-text");
        }

        public override void OnOpened()
        {
            RefreshText();
        }

        public override void OnGameTick(float dt)
        {
            RefreshText();
        }

        private void OnEnabledChanged(string code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            bool wantsOn = code == "On";
            bool isOn = DebugViewController.Instance.IsActive(viewId);
            if (wantsOn != isOn)
            {
                _ = DebugViewController.Instance.TryActivate(viewId, out _);
            }

            RefreshText();
        }

        private void OnSelectionChanged(string _code, bool selected)
        {
            if (!selected)
            {
                return;
            }

            RefreshText();
        }

        private void RefreshText()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return;
            }

            try
            {
                var dyn = composer.GetDynamicText($"{keyPrefix}-text");

                string prefix = GetPrefix();
                string? prefixFilter = string.IsNullOrEmpty(prefix) ? null : prefix;
                var sort = GetSort();
                var entries = GlGpuProfiler.Instance.GetSnapshot(sort, prefixFilter, maxEntries: 64);

                string enabled = GlGpuProfiler.Instance.Enabled ? "On" : "Off";
                int w = capi.Render.FrameWidth;
                int h = capi.Render.FrameHeight;
                string dumpSuffix = GetDumpStatusSuffix();

                if (entries.Length == 0)
                {
                    dyn.SetNewText($"GPU profiler: {enabled} @ {w}x{h}{dumpSuffix}\n(no events yet)");
                    return;
                }

                static string Ms(float v) => v <= 0f ? "-" : v.ToString("0.###");

                var lines = new string[entries.Length + 2];
                lines[0] = $"GPU profiler: {enabled} @ {w}x{h}  (showing {entries.Length}){dumpSuffix}";
                lines[1] = "Event | last ms | avg ms | min ms | max ms | n";

                for (int i = 0; i < entries.Length; i++)
                {
                    var e = entries[i];
                    var s = e.Stats;
                    lines[i + 2] = $"{e.Name} | {Ms(s.LastMs)} | {Ms(s.AvgMs)} | {Ms(s.MinMs)} | {Ms(s.MaxMs)} | {s.SampleCount}";
                }

                dyn.SetNewText(string.Join("\n", lines));
            }
            catch
            {
                // Ignore UI refresh errors.
            }
        }

        private string GetDumpStatusSuffix()
        {
            if (!string.IsNullOrWhiteSpace(lastDumpError))
            {
                return "  (dump failed)";
            }

            if (!string.IsNullOrWhiteSpace(lastDumpPath))
            {
                try
                {
                    return $"  (dumped {Path.GetFileName(lastDumpPath)})";
                }
                catch
                {
                    return "  (dumped)";
                }
            }

            return string.Empty;
        }

        private bool OnDumpJsonClicked()
        {
            try
            {
                var entries = GlGpuProfiler.Instance.GetSnapshot(
                    sort: GpuProfileSort.Name,
                    prefix: null,
                    maxEntries: int.MaxValue);

                var root = BuildHierarchy(entries);

                var snapshot = new GpuProfilerJsonSnapshot(
                    GeneratedUtc: DateTime.UtcNow,
                    Enabled: GlGpuProfiler.Instance.Enabled,
                    Width: capi.Render.FrameWidth,
                    Height: capi.Render.FrameHeight,
                    PrefixFilter: null,
                    Root: root);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                };

                string json = JsonSerializer.Serialize(snapshot, options);

                string dir = Path.Combine(GamePaths.DataPath, "VGE", "Profiling");
                Directory.CreateDirectory(dir);

                string filePath = Path.Combine(dir, $"gpu-profiler-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
                File.WriteAllText(filePath, json);

                lastDumpPath = filePath;
                lastDumpError = null;

                capi.Logger.Notification("[VGE] GPU profiler JSON written (all events): {0}", filePath);
            }
            catch (Exception e)
            {
                lastDumpError = e.GetType().Name;
                capi.Logger.Warning("[VGE] GPU profiler JSON dump failed: {0}", e);
            }

            RefreshText();
            return true;
        }

        private static GpuProfilerJsonNode BuildHierarchy(GpuProfileEntry[] entries)
        {
            var root = new GpuProfilerJsonNode(name: "(root)", path: string.Empty);

            var nodesByPath = new Dictionary<string, GpuProfilerJsonNode>(StringComparer.Ordinal)
            {
                [string.Empty] = root
            };

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    continue;
                }

                string[] scopeParts = entry.Name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (scopeParts.Length == 0)
                {
                    continue;
                }

                var segments = new List<string>(scopeParts.Length * 2);
                foreach (string scopePart in scopeParts)
                {
                    string[] dotParts = scopePart.Split('.', StringSplitOptions.RemoveEmptyEntries);
                    if (dotParts.Length == 0)
                    {
                        continue;
                    }

                    segments.AddRange(dotParts);
                }

                if (segments.Count == 0)
                {
                    continue;
                }

                GpuProfilerJsonNode parent = root;
                string path = string.Empty;
                foreach (string seg in segments)
                {
                    path = string.IsNullOrEmpty(path) ? seg : $"{path}/{seg}";

                    if (!nodesByPath.TryGetValue(path, out var node))
                    {
                        node = new GpuProfilerJsonNode(name: seg, path: path);
                        nodesByPath[path] = node;
                        parent.Children.Add(node);
                    }

                    parent = node;
                }

                parent.EventName = entry.Name;
                parent.Stats = entry.Stats;
            }

            SortTree(root);
            return root;
        }

        private static void SortTree(GpuProfilerJsonNode node)
        {
            if (node.Children.Count > 1)
            {
                node.Children.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
            }

            foreach (var child in node.Children)
            {
                SortTree(child);
            }
        }

        private sealed record GpuProfilerJsonSnapshot(
            DateTime GeneratedUtc,
            bool Enabled,
            int Width,
            int Height,
            string? PrefixFilter,
            GpuProfilerJsonNode Root);

        private sealed class GpuProfilerJsonNode
        {
            public GpuProfilerJsonNode(string name, string path)
            {
                Name = name;
                Path = path;
            }

            public string Name { get; }
            public string Path { get; }
            public string? EventName { get; set; }
            public GpuProfileStats? Stats { get; set; }
            public List<GpuProfilerJsonNode> Children { get; } = [];
        }

        private string GetPrefix()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return string.Empty;
            }

            string? val = composer.GetDropDown($"{keyPrefix}-cat").SelectedValue;
            return val switch
            {
                "PBR" => "PBR.",
                "LumOn" => "LumOn.",
                "Debug" => "Debug.",
                _ => string.Empty
            };
        }

        private GpuProfileSort GetSort()
        {
            if (composer is null || string.IsNullOrWhiteSpace(keyPrefix))
            {
                return GpuProfileSort.LastMs;
            }

            string? val = composer.GetDropDown($"{keyPrefix}-sort").SelectedValue;
            return val switch
            {
                "Name" => GpuProfileSort.Name,
                "LastMs" => GpuProfileSort.LastMs,
                "AvgMs" => GpuProfileSort.AvgMs,
                "MaxMs" => GpuProfileSort.MaxMs,
                _ => GpuProfileSort.LastMs
            };
        }
    }
}
