using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Projects completed GPU records while their read-only mapping is borrowed; the span must not escape.</summary>
internal delegate TResult GpuQueueReader<T, out TResult>(ReadOnlySpan<T> records) where T : unmanaged;
