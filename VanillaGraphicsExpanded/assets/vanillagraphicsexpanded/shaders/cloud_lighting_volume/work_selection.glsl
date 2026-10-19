/** Tests eligible channel demand while excluding already selected fair work. */
bool Eligible(uint job,uint channels)
{
    uint region=job/channels, channel=channels==1u?2u:job%2u, bit=1u<<channel;
    uint flags=regions[region*16u+1u];
    return ActiveRegion(region) && (flags&bit)!=0u && (flags&(bit<<16u))==0u;
}
/** Orders priority work by bucket, oldest pending frame, then deterministic lattice/channel index. */
bool Better(uint a,uint b,uint channels)
{
    uint ca=channels==1u?2u:a%2u,cb=channels==1u?2u:b%2u;
    uint ba=(a/channels)*16u,bb=(b/channels)*16u;
    uint fa=regions[ba+1u],fb=regions[bb+1u],bita=1u<<ca,bitb=1u<<cb;
    uint pa=(fa&(bita<<4u))!=0u?0u:(fa&(bita<<8u))!=0u?1u:(fa&VISIBLE)!=0u?2u:3u;
    uint pb=(fb&(bitb<<4u))!=0u?0u:(fb&(bitb<<8u))!=0u?1u:(fb&VISIBLE)!=0u?2u:3u;
    if(pa!=pb) return pa<pb;
    uint ta=regions[ba+5u+ca],tb=regions[bb+5u+cb];
    return ta!=tb?ta<tb:a<b;
}
/** Maintains a bounded worst-at-root heap of the best remaining jobs in the existing scratch allocation. */
void Offer(uint job,uint capacity,uint channels,inout uint count)
{
    if(count==capacity && !Better(job,scratch[HEAP],channels)) return;
    uint index;
    if(count<capacity)
    {
        index=count++;
        while(index>0u)
        {
            uint parent=(index-1u)/2u;
            if(!Better(scratch[HEAP+parent],job,channels)) break;
            scratch[HEAP+index]=scratch[HEAP+parent];index=parent;
        }
    }
    else
    {
        index=0u;
        for(;;)
        {
            uint child=index*2u+1u;
            if(child>=count) break;
            if(child+1u<count && Better(scratch[HEAP+child],scratch[HEAP+child+1u],channels)) child++;
            if(!Better(job,scratch[HEAP+child],channels)) break;
            scratch[HEAP+index]=scratch[HEAP+child];index=child;
        }
    }
    scratch[HEAP+index]=job;
}
/** Reserves half the work budget for a persistent cursor, then fills the remainder by bounded priority selection. */
uint Select(uint budget,uint channels,uint outputOffset,uint cursorWord)
{
    uint total=RegionCount()*channels, count=0u, cursor=scratch[cursorWord]%total, walked=0u;
    while(walked<total && count<budget/2u)
    {
        uint job=(cursor+walked)%total;walked++;
        if(!Eligible(job,channels)) continue;
        scratch[outputOffset+count++]=job;
        uint channel=channels==1u?2u:job%2u;
        regions[(job/channels)*16u+1u]|=1u<<(16u+channel);
    }
    scratch[cursorWord]=(cursor+walked)%total;
    uint priority=0u;
    for(uint job=0u;job<total;job++) if(Eligible(job,channels)) Offer(job,budget-count,channels,priority);
    for(uint i=0u;i<priority;i++)
    {
        uint job=scratch[HEAP+i];scratch[outputOffset+count+i]=job;
        uint channel=channels==1u?2u:job%2u;
        regions[(job/channels)*16u+1u]|=1u<<(16u+channel);
    }
    return count+priority;
}
/** Selects fixed-size job lists without CPU map readback or an unbounded dispatch. */
void main()
{
    if(gl_GlobalInvocationID.x!=0u) return;
    scratch[4]=Select(512u,2u,DIRECT_LIST,1u);
    scratch[5]=Select(256u,1u,DIFFUSE_LIST,2u);
    scratch[11]=0u; // Prior dependency demand has now been consumed by every region.
    for(uint region=0u;region<RegionCount();region++) regions[region*16u+14u]=0u;
}
