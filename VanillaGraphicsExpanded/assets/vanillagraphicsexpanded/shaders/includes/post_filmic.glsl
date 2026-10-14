#ifndef VGE_POST_FILMIC
#define VGE_POST_FILMIC
/** Encodes exposed linear RGB over a dark-detail-preserving 32-stop lookup domain. */
vec3 VgeGradingCoordinate(vec3 color)
{
    return clamp(log2(vec3(1.0)+max(color,vec3(0))*65536.0)/32.0,0.0,1.0);
}
/** Converts the lookup coordinate back to exposed scene-linear RGB, with exact black. */
vec3 VgeGradingRadiance(vec3 coordinate)
{
    return (exp2(coordinate*32.0)-1.0)/65536.0;
}
/** Converts linear D65 Rec.709 to the D60 AP1 grading primaries. */
vec3 VgeGradingToAp1(vec3 c)
{
    return vec3(dot(c,vec3(.6130974024,.3395231462,.0473794514)),
        dot(c,vec3(.0701937225,.9163538791,.0134523985)),
        dot(c,vec3(.0206155929,.1095697729,.8698146342)));
}
/** Returns the graded AP1 response to linear D65 Rec.709 for display encoding. */
vec3 VgeGradingFromAp1(vec3 c)
{
    return vec3(dot(c,vec3(1.7050509927,-.6217921207,-.0832588720)),
        dot(c,vec3(-.1302564175,1.1408047366,-.0105483191)),
        dot(c,vec3(-.0240033568,-.1289689761,1.1529723329)));
}
/** Evaluates a logarithmic film response with a soft toe, straight middle and bounded shoulder. */
vec3 VgeFilmicResponse(vec3 logColor)
{
    const float slope=.88, toeRange=.45, shoulderRange=.78;
    // Match 18% exposed gray to 18% output; solve the toe's logistic centre once.
    const float toeCentre=-.7447274949+.5*log(4.0)*toeRange/slope;
    const float straightOffset=.45/slope-toeCentre;
    const float shoulderCentre=.26/slope-straightOffset;
    vec3 straight=slope*(logColor+straightOffset);
    vec3 toe=.9/(vec3(1)+exp(clamp((-2.0*slope/toeRange)*(logColor-toeCentre),-80.0,80.0)));
    vec3 shoulder=1.04-1.56/(vec3(1)+exp(clamp((2.0*slope/shoulderRange)*(logColor-shoulderCentre),-80.0,80.0)));
    vec3 low=mix(straight,toe,lessThan(logColor,vec3(toeCentre)));
    vec3 high=mix(straight,shoulder,greaterThan(logColor,vec3(shoulderCentre)));
    vec3 weight=smoothstep(vec3(min(toeCentre,shoulderCentre)),vec3(max(toeCentre,shoulderCentre)),logColor);
    return clamp(mix(low,high,weight),0.0,1.0);
}
/** Grades exposed HDR in AP1, then resolves the film response and one sRGB display transfer. */
vec3 VgeGradedDisplay(vec3 radiance,vec4 controls)
{
    vec3 ap1=max(VgeGradingToAp1(max(radiance,vec3(0))),vec3(0));
    float contrast=max(1.0+controls.w,.01);
    float gamma=max(controls.x*controls.y,.001);
    // Logarithmic arithmetic prevents extreme grading powers from overflowing before the shoulder.
    vec3 logPower=(log2(max(ap1,vec3(1e-30))/.18)*contrast+log2(.18))/gamma;
    logPower+=log2(max(controls.z,1e-30));
    vec3 graded=exp2(clamp(logPower,-100.0,100.0));
    graded=mix(vec3(0),graded,greaterThan(ap1,vec3(0)));
    if(controls.z<=0.0) return vec3(0);
    const vec3 luma=vec3(.2722287168,.6740817658,.0536895174);
    graded=mix(vec3(dot(graded,luma)),graded,.96);
    vec3 film=VgeFilmicResponse(log2(max(graded,vec3(1e-30)))*.3010299957);
    film=mix(vec3(dot(film,luma)),film,.93);
    vec3 linear=clamp(VgeGradingFromAp1(film),0.0,1.0);
    // Exact black must not acquire the logarithm's numerical guard as a light source.
    if(max(radiance.r,max(radiance.g,radiance.b))<=0.0) return vec3(0);
    return VgeLinearToSrgb(linear);
}
#endif
