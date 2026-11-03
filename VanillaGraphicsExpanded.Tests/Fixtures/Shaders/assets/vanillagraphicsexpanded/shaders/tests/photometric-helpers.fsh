#version 450 core
#extension GL_ARB_shader_storage_buffer_object : require
#extension GL_ARB_shader_image_load_store : require
#extension GL_ARB_shading_language_420pack : require
@import "vanillagraphicsexpanded:shaders/includes/atmosphere_transport.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_photometric.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_frame_lighting.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_storage_safety.glsl"
@import "vanillagraphicsexpanded:shaders/includes/pbr_persistent_lighting.glsl"
@import "vanillagraphicsexpanded:shaders/includes/liquids/boundary_transport.glsl"

layout(std140, binding = 28) uniform PhotometricProbeInputs
{
    ivec4 control;
    vec4 solarScaleLimits[2];
};

layout(location = 0) out vec4 result;

/** Exposes direct production-helper quantities that the installed consumer programs compose before output. */
void main()
{
    int column = int(gl_FragCoord.x);
    int row = int(gl_FragCoord.y);
    if (control.x == 0)
    {
        vec3 origin = vec3(0, atmGround, 0);
        vec3 sun = normalize(vec3(1, -.002, 0));
        result = vec4(atmTransmission(origin, sun, 12), atmGroundDistance(origin, sun));
    }
    else if (control.x == 1)
    {
        precise vec3 origin = vec3(0, atmGround + .001, 0);
        float ground = atmGroundDistance(origin, vec3(0, -1, 0));
        result = vec4(ground, origin.y - atmGround, 0, 1);
    }
    else if (control.x == 2)
    {
        bool accessor = column % 2 == 1;
        result = vec4(VgeBlockIlluminance(vec3(accessor ? 127.0 / 255.0 : 128.0 / 255.0), accessor), 1);
    }
    else if (control.x == 3)
    {
        bool accessor = control.y != 0;
        float floorValue = accessor ? 9.0 / 255.0 : 10.0 / 255.0;
        float epsilon = .0001;
        float peak = column == 0 ? 0.0 : column == 1 ? floorValue : column == 2 ? floorValue + epsilon
            : column == 3 ? floorValue + 2.0 * epsilon : column == 6 ? .8 : 1.0;
        vec3 raw = column < 2 ? vec3(peak) : peak * vec3(1, .6, .3);
        if (column == 4) raw = vec3(38, 23, 14) / 255.0;
        if (column == 5) raw = (accessor ? vec3(144, 98, 70) : vec3(145, 99, 71)) / 255.0;
        result = vec4(VgeBlockIlluminance(raw, accessor), 1);
    }
    else if (control.x == 4)
    {
        const float peaks[8] = float[8](.08, .12, .2, .3, .4, .5, .8, 1.0);
        result = vec4(VgeBlockIlluminance(vec3(peaks[column])), 1);
    }
    else if (control.x == 5)
    {
        float floorValue = row == 0 ? 7.0 / 255.0 : 8.0 / 255.0;
        float raw = column == 0 ? 0.0 : column == 1 ? floorValue
            : column == 2 ? (floorValue + 1.0) * .5 : column == 3 ? 1.0 : 2.0;
        result = vec4(vec3(row == 1 ? VgeStoredSkyAvailability(raw) : VgeSkyAvailability(raw, false)), 1);
    }
    else if (control.x == 6)
    {
        if (column < 2)
            result = vec4(VgePointIlluminance(vec4(vec3(1000.0 / (4.0 * VGE_PHOTOMETRIC_PI)), 100),
                column == 0 ? 1.0 : 4.0), 1);
        else if (column == 2) result = vec4(VgePointIlluminance(vec4(4, 4, 4, 0), 1.0), 1);
        else if (column == 3) result = vec4(VgePointIlluminance(vec4(4, 4, 4, 0), 100.0), 1);
        else if (column == 4) result = vec4(VgeEmissionLuminance(vec3(.25, .5, 1), 2), 1);
        else result = vec4(VgeEmissionLuminance(vec3(.25, .5, 1), -1), 1);
    }
    else if (control.x == 7)
    {
        float infinityValue = uintBitsToFloat(0x7f800000u);
        float nanValue = uintBitsToFloat(0x7fc00000u);
        vec3 inputValue = column == 0 ? vec3(1, 2, 3) : column == 1 ? vec3(65504)
            : column == 2 ? vec3(70000) : column == 3 ? vec3(infinityValue, -infinityValue, 1)
            : column == 4 ? vec3(nanValue, 2, 3) : column == 5 ? vec3(-70000) : vec3(65024);
        float lower = row == 0 ? 0.0 : -65504.0;
        float upper = column == 6 ? 65024.0 : column == 7 ? 64512.0 : 65504.0;
        vec3 value;
        bool clipped;
        bool valid = VgeProtectLightingStorage(inputValue, lower, upper, value, clipped);
        result = vec4(value, valid ? (clipped ? 2.0 : 1.0) : 0.0);
    }
    else if (control.x == 8)
    {
        vec3 raw = column == 0 ? vec3(0) : column == 1 ? vec3(10.0 / 255.0)
            : column == 2 ? vec3(.2, .1, .05) : column == 3 ? vec3(.4, .2, .1)
            : column == 4 ? vec3(145, 99, 71) / 255.0 : column == 5 ? vec3(-1, .4, .2)
            : column == 6 ? vec3(1) : vec3(1048064);
        if (row == 0) result = vec4(VgeBlockIlluminance(raw), 1);
        else if (column < 6) result = vec4(VgeDiffuseIncident(VgeBlockIlluminance(raw)), 1);
        else
        {
            vec3 physical = column == 6 ? vec3(-32, 131072, 1048064) : vec3(1048065);
            result = vec4(VgeDecodePersistentLight(VgeEncodePersistentLight(physical)),
                VgePersistentLightAdmitted(physical, true) ? 1 : 0);
        }
    }
    else if (control.x == 9)
    {
        float tau = column == 0 ? 0.0 : column == 1 ? .0001 : column == 2 ? .5 : column == 3 ? 10.0 : 1.0;
        vec3 upper = vec3(.2, .3, .4), q = upper * tau;
        if (column == 4) q *= 100.0;
        if (column == 5) q = -q;
        if (column == 6) upper.x = -1.0;
        if (column == 7) { upper = vec3(3e38); tau = 100.0; }
        vec3 transmission, scatter;
        float waterLength;
        bool valid = VgeWaterBoundaryTransport(vec4(vec3(tau), 1), vec4(q, 0), vec3(0), upper,
            false, 1000.0, transmission, scatter, waterLength);
        result = vec4(row == 0 ? scatter : transmission, valid ? 1 : 0);
    }
    else if (control.x == 10)
    {
        vec3 value = vec3(0);
        bool valid = true;
        if (column == 0) value = VgeStoreFrameLight(vec3(32));
        else if (column == 1) value = VgePhysicalFrameLight(vec3(8));
        else if (column == 2) value = VgePersistentToFrameLight(vec3(2));
        else if (column < 6) valid = VgeRescaleFrameHistory(vec3(2),
            column == 3 ? .0625 : column == 4 ? 0.0 : row == 1 ? 3e38 : .00001, 32.0, value);
        else valid = VgeFrameLightingValid(42u, column == 6 ? 7u : 8u);
        result = vec4(value, valid ? 1 : 0);
    }
    else if (control.x == 11)
    {
        if (row == 0)
        {
            vec3 physical = column == 3 ? vec3(-1) : column == 4 ? vec3(uintBitsToFloat(0x7fc00000u))
                : column == 5 ? vec3(uintBitsToFloat(0x7f800000u)) : column == 6 ? vec3(1e-20)
                : column == 7 ? vec3(uintBitsToFloat(0x7f7fffffu), uintBitsToFloat(0x7f7fffffu) * .5, 0)
                : vec3(3e38, 1.5e38, 0);
            vec3 value;
            bool valid = VgeStoreVisibleSolarDisk(physical, solarScaleLimits[column / 4][column % 4], value);
            result = vec4(value, valid ? 1 : 0);
        }
        else
        {
            vec3 physical = column == 0 ? vec3(131072) : column == 1 ? vec3(131073) : column == 2 ? vec3(-1)
                : column == 3 ? vec3(uintBitsToFloat(0x7fc00000u)) : vec3(0);
            result = vec4(vec3(0), VgeDirectionalLightAdmitted(physical) ? 1 : 0);
        }
    }
    else result = vec4(0);
}
