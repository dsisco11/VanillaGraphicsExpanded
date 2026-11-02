/// Checks correlated authored samples without reproducing the production illumination equation.
bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
    if(coordinate.y!=0) return bvec2(true,false);
    int column=coordinate.x;
    uint valid=1u;
    if(predicateChoice==1) {
        if(column>0) valid &= uint(a.r>texelFetch(actualImage,ivec3(column-1,0,0),0).r);
        return bvec2(valid!=0u,false);
    }
    if(column<2) valid &= uint(all(equal(a.rgb,vec3(0))));
    if(column==2) valid &= uint(a.r>=0.0 && a.r<=.0000000000001);
    if(column==3) {
        float ratio=a.r/texelFetch(actualImage,ivec3(2,0,0),0).r;
        valid &= uint(ratio>=15.97 && ratio<=16.03);
    }
    if(column>=2) {
        valid &= uint(a.r>texelFetch(actualImage,ivec3(column-1,0,0),0).r);
        valid &= uint(!any(isnan(a.rgb)) && !any(isinf(a.rgb)));
    }
    if(column==2 || column==3 || column==6 || column==7) {
        float green=a.g/a.r,blue=a.b/a.r;
        valid &= uint(green>=.59999 && green<=.60001);
        valid &= uint(blue>=.29999 && blue<=.30001);
    }
    // The original CPU oracle accumulates in double and rounds luminance to float before comparison.
    precise double luminance=.2126lf*double(a.r)+.7152lf*double(a.g)+.0722lf*double(a.b);
    if(column==5) {
        double expected=44.0lf/(4.0lf*3.141592653589793lf);
        double actual=double(float(luminance));
        valid &= uint(!isnan(actual) && !isinf(actual));
        valid &= uint(actual>=expected-2e-6lf*max(1.0lf,abs(expected)));
        valid &= uint(actual<=expected+2e-6lf*max(1.0lf,abs(expected)));
    }
    if(column==4 && multipliers.x==0.0) valid &= uint(luminance>=.019228lf && luminance<=.019232lf);
    return bvec2(valid!=0u,false);
}
