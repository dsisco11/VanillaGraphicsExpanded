#ifndef VGE_VERTEX_FLAGS_GLSL
#define VGE_VERTEX_FLAGS_GLSL
/** Engine liquid vertex bit layout; independent of shader-source injection. */

const int GlowLevelBitMask = 0xFF;

const int ZOffsetBitMask = 0x7 << 8;

const int ReflectiveBitMask = 1 << 11;

const int Lod0BitMask = 1 << 12;

const int NormalBitMask = 0xFFF << 13;

const int WindModeBitMask = 0xF << 25;

const int WindModePosition = 25;

const int LiquidWaterModeBitMask = 0xF << 25;

const int LiquidExposedToSkyBitMask = 1 << 29;

const int WindDataBitMask = 0x7 << 29;

const int WindDataPosition = 29;

const int WindBitsMask = WindModeBitMask | WindDataBitMask;

const int WindModeWeakMask = 1 << 25;
const int WindModeNormalMask = 2 << 25;
const int WindModeLeavesMask = 3 << 25;
const int WindModeBendMask = 4 << 25;
const int WindModeTallBendMask = 5 << 25;
const int WindModeWaterMask = 6 << 25;
const int WindModeExtraWeakMask = 7 << 25;
const int WindModeFruitMask = 8 << 25;
const int WindModeWeakWindNoBendMask = 9 << 25;
const int WindModeWeakWindInversedBendMask = 10 << 25;
const int WindModeWaterPlant = 11 << 25;
const int WindModeLiquidWarp = 12 << 25;
const int WindModeWeakLowAlphaTest = 13 << 25;

const int ReflectiveModeWeak = 0;
const int ReflectiveModeMedium = 1;
const int ReflectiveModeStrong = 2;
const int ReflectiveModeSparkly = 3;
const int ReflectiveModeMild = 4;

const int LiquidIsLavaBitPosition = 27;
const int LiquidWeakFoamBitPosition = 28;
const int LiquidWeakWavePosition = 29;
const int LiquidFullAlphaBitPosition = 30;
const int LiquidSkyExposedBitPosition = 31;

const int LiquidIsLavaBitMask = 1 << LiquidIsLavaBitPosition;
const int LiquidWeakFoamBitMask = 1 << LiquidWeakFoamBitPosition;
const int LiquidWeakWaveBitMask = 1 << LiquidWeakWavePosition;
const int LiquidFullAlphaBitMask = 1 << LiquidFullAlphaBitPosition;
const int LiquidSkyExposedBitMask = 1 << LiquidSkyExposedBitPosition;

const float OneOver255 = 1.0 / 255.0;

/** Decodes the engine packed signed normal components. */
vec3 unpackNormal(int flags) {
	int x = (flags >> (13+1)) & 0x7;
	int y = (flags >> (13+5)) & 0x7;
	int z = (flags >> (13+9)) & 0x7;
	
	int signx = (flags >> 12) & 2;
	int signy = (flags >> (12+4)) & 2;
	int signz = (flags >> (12+8)) & 2;
	
	return normalize(vec3(
		(1.0 - signx) * x / 7.0,
		(1.0 - signy) * y / 7.0,
		(1.0 - signz) * z / 7.0
	));
}



#endif
