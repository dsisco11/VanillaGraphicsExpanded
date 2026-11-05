#version 430
layout(local_size_x=1) in;
layout(std430,binding=4) buffer DiagnosticCounters { uint counters[]; };

/** Writes one known diagnostic record for collector submission, fence and readback coverage. */
void main()
{
    // SurfaceWorkCounter.Texels and SurfaceWorkCounter.Completed occupy offsets zero and one.
    counters[0] = 64u;
    counters[1] = 1u;
}
