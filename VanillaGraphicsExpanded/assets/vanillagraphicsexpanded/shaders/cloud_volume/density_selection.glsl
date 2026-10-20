// Temporary selection data shares the lighting reduction area; the owners never select concurrently.
const int DensityHistogram=32768,DensityFairCounts=245760,DensityPriorityCounts=246272;
shared uint densityScan[256];
shared uint densityBefore;

/** Computes a stable inclusive prefix with all lanes participating, including padded final groups. */
uint DensityPrefix(uint value)
{
    uint lane=gl_LocalInvocationIndex;densityScan[lane]=value;barrier();
    for(uint stride=1u;stride<256u;stride*=2u)
    {
        uint add=lane>=stride?densityScan[lane-stride]:0u;
        barrier();densityScan[lane]+=add;barrier();
    }
    return densityScan[lane];
}
/** Reduces bounded chunk counts rather than visiting every region from one invocation. */
uint DensityGroupBefore(int base,int group)
{
    if(gl_LocalInvocationIndex==0u)
    {uint sum=0u;for(int i=0;i<group;i++)sum+=scratch[base+i];densityBefore=sum;}
    barrier();return densityBefore;
}
/** Orders unsigned waiting ages from oldest to youngest, preserving the existing wrap convention. */
uint DensityAgeKey(uint region){return 0xffffffffu-(uint(volumeStamps.x)-regions[region*16u+3u]);}
/** Selects threshold ties by physical region identity after all older pending candidates. */
bool DensityPriorityCandidate(uint id)
{
    int group=int(gl_WorkGroupID.x),bucket=int(scratch[36]&255u);
    if(gl_LocalInvocationIndex==0u)
    {uint sum=0u;for(int i=0;i<group;i++)sum+=scratch[DensityHistogram+i*256+bucket];densityBefore=sum;}
    barrier();uint priorTies=densityBefore;
    bool pending=id<uint(volumeStamps.z)&&(regions[id*16u]&(1u|256u))==1u;
    uint key=pending?DensityAgeKey(id):0xffffffffu;
    uint tie=DensityPrefix(pending&&key==scratch[36]?1u:0u);
    return scratch[34]!=0u&&pending&&(key<scratch[36]||(key==scratch[36]&&priorTies+tie<=scratch[37]));
}
/** Executes a structural stage of stable fair selection and four-byte age-threshold reduction. */
void DensitySelect()
{
    uint id=gl_GlobalInvocationID.x,lane=gl_LocalInvocationIndex;
    int group=int(gl_WorkGroupID.x),groups=(volumeStamps.z+255)/256;
    #if VGE_VOLUME_DENSITY_OPERATION==2
    if(id>=uint(volumeStamps.z))return;
    uint b=id*16u,flags=regions[b];bool pending=(flags&1u)!=0u;
    if((flags&16u)!=0u||(pending&&regions[b+1u]==0u))
    {
        if(!pending||regions[b+1u]==0u)regions[b+3u]=uint(volumeStamps.w!=0?volumeSettings.z:volumeStamps.x);
        regions[b+1u]=uint(volumeStamps.y);flags|=1u;
    }
    regions[b]=flags&~(16u|256u);
    #elif VGE_VOLUME_DENSITY_OPERATION==6
    uint region=(scratch[5]+id)%uint(volumeStamps.z);
    uint sum=DensityPrefix(id<uint(volumeStamps.z)&&(regions[region*16u]&1u)!=0u?1u:0u);
    if(lane==255u)scratch[DensityFairCounts+group]=sum;
    #elif VGE_VOLUME_DENSITY_OPERATION==8
    uint count=0u;for(int i=int(lane);i<groups;i+=256)count+=scratch[DensityFairCounts+i];
    uint sum=DensityPrefix(count);
    if(lane==255u)
    {
        uint fair=min(sum,uint(volumeStamps.w!=0?volumeSettings.x:volumeSettings.x/2));
        scratch[32]=scratch[5]%uint(volumeStamps.z);scratch[33]=fair;
        scratch[34]=min(sum-fair,uint(volumeSettings.x)-fair);
        scratch[35]=0u;scratch[36]=0u;scratch[37]=scratch[34];summary[6]=sum;
    }
    #elif VGE_VOLUME_DENSITY_OPERATION==7
    uint region=(scratch[32]+id)%uint(volumeStamps.z);
    bool pending=id<uint(volumeStamps.z)&&(regions[region*16u]&1u)!=0u;
    uint before=DensityGroupBefore(DensityFairCounts,group),rank=before+DensityPrefix(pending?1u:0u);
    if(pending&&rank<=scratch[33])
    {
        scratch[64u+rank-1u]=region;regions[region*16u]|=256u;
        if(rank==scratch[33])scratch[5]=(region+1u)%uint(volumeStamps.z);
    }
    #elif VGE_VOLUME_DENSITY_OPERATION==9
    densityScan[lane]=0u;barrier();
    if(id<uint(volumeStamps.z)&&(regions[id*16u]&(1u|256u))==1u)
    {
        uint key=DensityAgeKey(id);
        if((key&scratch[35])==scratch[36])atomicAdd(densityScan[(key>>uint(volumeSettings.w))&255u],1u);
    }
    barrier();scratch[DensityHistogram+group*256+int(lane)]=densityScan[lane];
    #elif VGE_VOLUME_DENSITY_OPERATION==10
    uint count=0u;for(int i=0;i<groups;i++)count+=scratch[DensityHistogram+i*256+int(lane)];
    uint end=DensityPrefix(count),rank=scratch[37],before=end-count;
    if(rank>before&&rank<=end)
    {
        scratch[36]|=lane<<uint(volumeSettings.w);scratch[35]|=255u<<uint(volumeSettings.w);
        scratch[37]=rank-before;
    }
    #elif VGE_VOLUME_DENSITY_OPERATION==11
    uint sum=DensityPrefix(DensityPriorityCandidate(id)?1u:0u);
    if(lane==255u)scratch[DensityPriorityCounts+group]=sum;
    #elif VGE_VOLUME_DENSITY_OPERATION==12
    bool selected=DensityPriorityCandidate(id);
    uint before=DensityGroupBefore(DensityPriorityCounts,group),rank=before+DensityPrefix(selected?1u:0u);
    if(selected){scratch[64u+scratch[33]+rank-1u]=id;regions[id*16u]|=256u;}
    #elif VGE_VOLUME_DENSITY_OPERATION==13
    if(id!=0u)return;
    uint count=scratch[33]+scratch[34];scratch[4]=count;summary[4]=0u;summary[5]=count;
    commands[0]=count;commands[1]=1u;commands[2]=1u;
    #endif
}
