// Persistent ranges lie above the largest lighting reduction and below the fixed scratch end.
const int OccupiedCounts=970000,OccupiedOffsets=970288,OccupiedList=970576;
#if VGE_VOLUME_WORK_OPERATION==7 || VGE_VOLUME_WORK_OPERATION==9
shared uint occupiedScan[256];
/** Computes a stable inclusive prefix within one physical-region group. */
uint OccupiedPrefix(uint value)
{
    uint lane=gl_LocalInvocationIndex;occupiedScan[lane]=value;barrier();
    for(uint step=1u;step<256u;step*=2u)
    {
        uint add=lane>=step?occupiedScan[lane-step]:0u;barrier();
        occupiedScan[lane]+=add;barrier();
    }
    return occupiedScan[lane];
}
#endif
/** Maintains the stable compact membership only after density or circular maintenance changes it. */
void OccupancySchedule()
{
    uint lane=gl_LocalInvocationIndex,id=gl_GlobalInvocationID.x;
    #if VGE_VOLUME_WORK_OPERATION==6
    if(id!=0u)return;
    bool rebuild=scratch[48]!=0u||scratch[51]==0u;
    uint groups=uint((workExtent.w+255)/256);
    commands[36]=rebuild?groups:0u;commands[37]=1u;commands[38]=1u;
    commands[39]=rebuild?groups:0u;commands[40]=1u;commands[41]=1u;
    commands[42]=rebuild?1u:0u;commands[43]=1u;commands[44]=1u;
    #elif VGE_VOLUME_WORK_OPERATION==7
    uint count=OccupiedPrefix(id<uint(workExtent.w)&&Origins(int(id))!=0u?1u:0u);
    if(lane==255u)scratch[OccupiedCounts+int(gl_WorkGroupID.x)]=count;
    #elif VGE_VOLUME_WORK_OPERATION==8
    if(id!=0u)return;
    uint total=0u;
    for(int group=0;group<(workExtent.w+255)/256;group++)
    {scratch[OccupiedOffsets+group]=total;total+=scratch[OccupiedCounts+group];}
    scratch[49]=total;scratch[48]=0u;scratch[50]=uint(workStamps.x);
    scratch[51]=scratch[51]==0xffffffffu?1u:scratch[51]+1u;
    #elif VGE_VOLUME_WORK_OPERATION==9
    bool member=id<uint(workExtent.w)&&Origins(int(id))!=0u;
    uint rank=OccupiedPrefix(member?1u:0u);
    if(member)scratch[OccupiedList+scratch[OccupiedOffsets+int(gl_WorkGroupID.x)]+rank-1u]=id;
    #elif VGE_VOLUME_WORK_OPERATION==10
    if(id!=0u)return;
    uint direct=workStamps.z!=0?(scratch[49]*2u+255u)/256u:0u;
    uint diffuse=workSettings.y!=0?(scratch[49]+255u)/256u:0u;
    // Fixed bounded host submission uses these GPU dimensions without reading list length.
    // Once a channel reaches one group, subsequent reductions retain its same winner set.
    for(int level=0;level<6;level++)
    {
        scratch[52+level*2]=direct;scratch[53+level*2]=diffuse;
        commands[18+level*3]=direct+diffuse;commands[19+level*3]=1u;commands[20+level*3]=1u;
        direct=(direct+3u)/4u;diffuse=(diffuse+3u)/4u;
    }
    #endif
}
