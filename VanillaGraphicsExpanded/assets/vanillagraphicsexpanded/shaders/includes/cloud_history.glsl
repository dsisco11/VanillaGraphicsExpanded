uniform sampler2D history0;
uniform sampler2D history1;
uniform sampler2D history2;
uniform sampler2D history3;
uniform sampler2D history4;
uniform sampler2D history5;
/** Fetches an unfiltered physical history channel without mixing unrelated depth records. */
vec4 CloudHistoryFetch(int layer,ivec2 pixel)
{
    if(layer==0) return texelFetch(history0,pixel,0);
    if(layer==1) return texelFetch(history1,pixel,0);
    if(layer==2) return texelFetch(history2,pixel,0);
    if(layer==3) return texelFetch(history3,pixel,0);
    if(layer==4) return texelFetch(history4,pixel,0);
    return texelFetch(history5,pixel,0);
}
