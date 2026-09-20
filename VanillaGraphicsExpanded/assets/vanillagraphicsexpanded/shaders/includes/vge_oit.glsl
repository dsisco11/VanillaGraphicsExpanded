layout(location = 0) out vec4 outOitRevealBins;
layout(location = 1) out vec4 outRevealage;
layout(location = 3) out vec4 outOitAccumulation0;
layout(location = 4) out vec4 outOitAccumulation1;
layout(location = 5) out vec4 outOitAccumulation2;

const float VGE_OIT_BIN_SCALE = 30.0;

float vgeOitBellCurve(float t)
{
    float n = t / 0.832;
    return exp(-n * n);
}

void writeOit(vec4 color)
{
    float depth = ((gl_FragCoord.z * 2.0) - 1.0) / gl_FragCoord.w;
    float bin = log((depth / VGE_OIT_BIN_SCALE) + 1.0);
    vec4 weightedColor = color * exp(-depth / 3000.0);

    float bin0 = vgeOitBellCurve(bin);
    float bin1 = vgeOitBellCurve(bin - 1.0);
    float bin2 = vgeOitBellCurve(bin - 2.0);
    if (bin > 2.0)
    {
        bin2 = 1.0;
    }

    outOitAccumulation0 = weightedColor * bin0;
    outOitAccumulation1 = weightedColor * bin1;
    outOitAccumulation2 = weightedColor * bin2;
    outOitRevealBins = vec4(
        1.0 - color.a * bin0,
        1.0 - color.a * bin1,
        1.0 - color.a * bin2,
        1.0);
    outRevealage = vec4(1.0 - color.a);
}
