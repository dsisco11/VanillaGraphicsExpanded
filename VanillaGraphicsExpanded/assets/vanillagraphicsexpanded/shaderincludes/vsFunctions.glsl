#ifndef VGE_VSFUNCTIONS_GLSL
#define VGE_VSFUNCTIONS_GLSL
/*
* Returns the metallic value based on the VintageStory render flags.
*/
float getMatMetallicFromRenderFlags(int renderFlags) {
    if ((renderFlags & ReflectiveBitMask) == 0) return 0.0;
    
    int windMode = (renderFlags >> 29) & 0x7;
    
    if (windMode == ReflectiveModeWeak) return 0.25;
    if (windMode == ReflectiveModeMild) return 0.3;
    if (windMode == ReflectiveModeMedium) return 0.5;
    if (windMode == ReflectiveModeSparkly) return 0.6;
    if (windMode == ReflectiveModeStrong) return 0.8;
    if (windMode == 5) return 1.0;
    
    return 0.0;
}
#endif // VGE_VSFUNCTIONS_GLSL