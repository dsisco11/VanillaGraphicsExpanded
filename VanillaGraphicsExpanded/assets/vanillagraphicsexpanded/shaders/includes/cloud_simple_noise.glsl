// Native-compatible gradient noise for mesh undulation and perception only.
/** Reproduces the installed polynomial gradient hash for stable native cloud motion. */
vec3 CloudGradient(vec3 p)
{
    vec3 o = vec3(127.1*p.x+311.7*p.y+74.7*p.z,269.5*p.x+183.3*p.y+246.1*p.z,113.5*p.x+271.9*p.y+124.6*p.z);
    vec3 q = (o*.025+8.0)*o;
    return -1.0+2.0*fract(mod(q,289.0)/41.0);
}
/** Interpolates native gradient noise without generating a volume or adding density detail. */
float CloudNoise(vec3 p)
{
    vec3 i=floor(p), f=p-i, u=f*f*(3.0-2.0*f);
    vec4 a=vec4(dot(CloudGradient(i),f), dot(CloudGradient(i+vec3(0,0,1)),f-vec3(0,0,1)),
        dot(CloudGradient(i+vec3(0,1,0)),f-vec3(0,1,0)),dot(CloudGradient(i+vec3(0,1,1)),f-vec3(0,1,1)));
    vec4 b=vec4(dot(CloudGradient(i+vec3(1,0,0)),f-vec3(1,0,0)),dot(CloudGradient(i+vec3(1,0,1)),f-vec3(1,0,1)),
        dot(CloudGradient(i+vec3(1,1,0)),f-vec3(1,1,0)),dot(CloudGradient(i+vec3(1,1,1)),f-vec3(1,1,1)));
    vec4 c=mix(a,b,u.x); vec2 d=mix(c.xy,c.zw,u.y);
    return 1.2*mix(d.x,d.y,u.z);
}
