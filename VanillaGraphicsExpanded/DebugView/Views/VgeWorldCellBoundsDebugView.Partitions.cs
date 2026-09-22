using System;
using System.Linq;
using System.Numerics;
using System.Text;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.WorldPartition;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Built-in debug views for registered spatial partitions.</summary>
public static partial class VgeBuiltInDebugViews
{
    /// <summary>Draws partition demand and readiness in camera-relative double-precision coordinates.</summary>
    private sealed partial class VgeWorldCellBoundsWireframeRenderer
    {
        private PartitionDiagnosticSnapshot[] partitionSnapshots = [];
        private long nextPartitionRefresh;

        #region Partition observations
        /// <summary>Refreshes detached statistics at four hertz and draws current bounds every frame.</summary>
        private void AddPartitionDiagnostics(ref int written, Vec3d camera)
        {
            long now = Environment.TickCount64;
            if (now >= nextPartitionRefresh)
            {
                nextPartitionRefresh = now + 250;
                partitionSnapshots = capi.ModLoader.GetModSystem<WorldPartitionModSystem>()?.GetCoordinator().Diagnostics() ?? [];
                var text = new StringBuilder();
                var selected = partitionSnapshots.Length == 0 ? null : partitionSnapshots[WorldCellBoundsViewState.SelectedPartition % partitionSnapshots.Length];
                if (selected is { } partition)
                {
                    var s = partition.Statistics;
                    text.AppendLine($"{partition.Name} #{partition.Instance} ({partition.World})");
                    text.AppendLine($"Required ready {partition.RequiredReady}/{s.Required}; resident {s.Resident}; ready {s.Ready}");
                    text.AppendLine($"Dirty {s.Dirty}; queued {s.Queued}; flight {s.InFlight}; backlog C/U {s.CaptureBacklog}/{s.UploadBacklog}");
                    text.AppendLine($"Retries {s.Retries}; stale {s.StaleCompletions}; shortfall {s.CapacityShortfall} cells / {s.UploadBudgetShortfall} B");
                    text.AppendLine($"Wait ticks: oldest {partition.OldestRequiredWaitTicks}; mean {partition.MeanPublicationWaitTicks:F1}; max {partition.MaximumPublicationWaitTicks}");
                }
                else text.AppendLine("No registered partitions.");
                if (capi.ModLoader.GetModSystem<LumOnModSystem>()?.GeometryMetrics is { } metrics && selected?.Instance == metrics.Instance)
                {
                    text.AppendLine($"Shared reads {metrics.SourceReads}; workers {metrics.SourceInFlight}");
                    text.AppendLine($"Demand Near/Surface {metrics.NearRequired}/{metrics.SurfaceRequired}; overlap {metrics.Overlap}; staged {metrics.StagedPayloadBytes} B");
                    text.AppendLine($"Snapshots {metrics.SnapshotBytes / 1048576d:F1} MiB; textures {metrics.TextureBytes / 1048576d:F1} MiB");
                    text.AppendLine($"Uploaded {metrics.UploadedBytes / 1048576d:F1} MiB; published {metrics.PublishedCells}");
                    text.AppendLine($"Update {metrics.LastUpdateMilliseconds:F2} ms; mean {metrics.MeanUpdateMilliseconds:F2}; peak {metrics.PeakUpdateMilliseconds:F2}");
                }
                WorldCellBoundsViewState.DiagnosticsText = text.ToString();
            }
            foreach (var partition in partitionSnapshots)
            {
                if (partition.Instance != partitionSnapshots[WorldCellBoundsViewState.SelectedPartition % partitionSnapshots.Length].Instance) continue;
                // Draw envelopes first so the vertex cap cannot hide the coverage policy itself.
                foreach (var source in partition.Sources)
                {
                    AddPartitionBox(ref written, source.Required, camera, Vector4.One);
                    var range = partition.Layout.Range(source.Required);
                    if (!range.Empty)
                        AddPartitionBox(ref written, new(partition.Layout.Bounds(range.Min).Min,
                            partition.Layout.Bounds(new(range.End.X - 1, range.End.Y - 1, range.End.Z - 1)).Max),
                            camera, new(0, 1, 1, 1));
                }
                foreach (var cell in partition.Cells)
                {
                    if (!WorldCellBoundsViewState.ShowUnloaded && cell.Actual == PartitionResidency.Unloaded) continue;
                    var bounds = partition.Layout.Bounds(cell.Key.Coordinate);
                    double radius = WorldCellBoundsViewState.RadiusChunks * 32d;
                    if (bounds.Max.X < camera.X - radius || bounds.Min.X > camera.X + radius ||
                        bounds.Max.Y < camera.Y - radius || bounds.Min.Y > camera.Y + radius ||
                        bounds.Max.Z < camera.Z - radius || bounds.Min.Z > camera.Z + radius) continue;
                    bool required = cell.Desired == PartitionResidency.Active;
                    Vector4 color = WorldCellBoundsViewState.ColorByDesiredState
                        ? (required ? new(1, .7f, 0, 1) : new(.5f, .3f, 1, 1))
                        : cell.ContentStatus == PartitionContentStatus.Unsupported && cell.Ready ? new(1, 0, 1, 1)
                        : cell.Ready ? new(0, required ? 1 : .5f, 0, 1)
                        : required ? new(1, .4f, 0, 1) : new(.5f, 0, 1, 1);
                    AddPartitionBox(ref written, bounds, camera, color);
                }
            }
        }

        /// <summary>Subtracts the camera before conversion to floats and respects the shared line capacity.</summary>
        private void AddPartitionBox(ref int written, in PartitionBounds bounds, Vec3d camera, Vector4 color)
        {
            if (written + 24 > vertices.Length) return;
            AddBoxLines(ref written, (float)(bounds.Min.X - camera.X), (float)(bounds.Min.Y - camera.Y),
                (float)(bounds.Min.Z - camera.Z), (float)(bounds.Max.X - camera.X),
                (float)(bounds.Max.Y - camera.Y), (float)(bounds.Max.Z - camera.Z), color.X, color.Y, color.Z, color.W);
        }
        #endregion
    }
}
