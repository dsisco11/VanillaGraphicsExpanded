uniform sampler2DArray historyPayload;
/** Fetches a block-local physical payload layer without exposing another block's writable images. */
vec4 CloudHistoryFetch(int layer,ivec2 pixel)
{
    return texelFetch(historyPayload,ivec3(pixel,layer),0);
}
