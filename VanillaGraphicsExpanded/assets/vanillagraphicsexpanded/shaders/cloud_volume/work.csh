#version 430 core
layout(local_size_x=256) in;
layout(std140) uniform VolumeWork
{
    ivec4 workExtent;
    ivec4 workStamps;
    ivec4 workSlots;
    vec4 workFirst;
    vec4 workCell;
    ivec4 workSettings;
    ivec4 workReduction; // source base, destination base, source direct groups, source diffuse groups
    vec4 workPlanes[6];
};
layout(std430) buffer WorkRegions {uint regions[];};
layout(std430) buffer WorkScratch {uint scratch[];};
layout(std430) buffer WorkCommands {uint commands[];};
layout(std430) readonly buffer WorkOccupancy {uint occupancy[];};
const int DirectJobs=16384,DiffuseJobs=16640;
const uint Invalid=0xffffffffu;
#if VGE_VOLUME_WORK_LARGE_BUDGET
const int SelectionEntries=1024;
#else
const int SelectionEntries=256;
#endif
shared uvec3 keys[SelectionEntries];
shared uint pendingCount;

/** Uses a conservative native-local AABB, treating a block spanning the logical seam as visible. */
bool Visible(int region)
{
    ivec3 block=ivec3(region%workExtent.x,(region/workExtent.x)%workExtent.y,region/(workExtent.x*workExtent.y));
    ivec3 p=block*ivec3(8,4,8),size=ivec3(workSlots.z,66,workSlots.z);
    ivec3 width=min(ivec3(8,4,8),size-p);
    ivec3 q=ivec3((p.x-workSlots.x+size.x)%size.x,p.y,(p.z-workSlots.y+size.z)%size.z);
    if(q.x+width.x>size.x||q.z+width.z>size.z)return true;
    vec3 lo=workFirst.xyz+vec3(q)*workCell.xyz,hi=lo+vec3(width)*workCell.xyz;
    for(int i=0;i<6;i++)if(dot(workPlanes[i].xyz,mix(lo,hi,greaterThanEqual(workPlanes[i].xyz,vec3(0))))+workPlanes[i].w<0)return false;
    return true;
}
/** Tests per-source targets independently, without consuming another channel's pending work. */
bool Pending(int job,int kind)
{
    int region=kind==0?job/2:job;
    // Retain demand without spending transport jobs on certified empty space. A later
    // density publication makes the same pending revisions eligible automatically.
    if(occupancy[region]==0u)return false;
    if(kind==1)return workSettings.y!=0&&(regions[region*16]&4u)!=0u;
    int source=job%2;
    return (workStamps.z&(1<<source))!=0&&(regions[region*16+5+source*2]!=regions[region*16+6+source*2]||regions[region*16+6+source*2]==0u);
}
/** Calculates each candidate's priority and persistent age once, before hierarchical reduction. */
uvec3 PriorityKey(uint job,int kind)
{
    int region=kind==0?int(job)/2:int(job),source=int(job)%2;
    uint complete=regions[region*16+(kind==0?6+source*2:13)];
    uint priority=complete==0u?0u:(kind==0&&(workSettings.x&(1<<source))!=0?1u:(Visible(region)?2u:3u));
    uint age=uint(workStamps.x)-regions[region*16+(kind==0?9:14)];
    return uvec3(priority,Invalid-age,job);
}
/** Orders pending jobs by cyclic distance from the persistent fair cursor. */
uvec3 FairKey(uint job,int kind)
{
    if(job==Invalid)return uvec3(Invalid);
    uint total=uint(workExtent.w*(kind==0?2:1)),cursor=scratch[18+kind]%total;
    return uvec3((job+total-cursor)%total,0u,job);
}
/** Compares fixed integer keys without SSBO lookups or repeated frustum tests. */
bool Less(uvec3 a,uvec3 b){return a.x<b.x||(a.x==b.x&&(a.y<b.y||(a.y==b.y&&a.z<b.z)));}
/** Sorts bounded shared keys cooperatively, retaining the default 256-entry specialization. */
void Sort(int count)
{
    int lane=int(gl_LocalInvocationIndex);
    barrier();
    for(int width=2;width<=count;width*=2)for(int stride=width/2;stride>0;stride/=2)
    {
        uvec3 chosen[SelectionEntries/256];
        for(int j=0;j<SelectionEntries/256;j++)
        {
            int index=lane+j*256;
            if(index<count)
            {
                uvec3 own=keys[index],other=keys[index^stride];
                bool minimum=((index&stride)==0)==((index&width)==0);
                chosen[j]=minimum?(Less(other,own)?other:own):(Less(own,other)?other:own);
            }
        }
        barrier();
        for(int j=0;j<SelectionEntries/256;j++)if(lane+j*256<count)keys[lane+j*256]=chosen[j];
        barrier();
    }
}
/** Returns block-job capacities from the admitted ray budget, preserving fair service for odd counts. */
int Budget(int kind){return int(workFirst.w)*(kind==0?2:1);}
/** Assigns the odd leftover job to fair service so the smallest budget cannot starve it. */
int FairQuota(int kind){return (Budget(kind)+1)/2;}
/** Accounts for exact fair indices, three-word priority keys and one backlog word. */
int SummaryStride(int kind){return FairQuota(kind)+Budget(kind)*3+1;}
/** Locates variable-size fair indices, priority keys and backlog within fixed maximum scratch. */
int Summary(int base,int kind,int group,int directGroups)
{return base+(kind==0?group*SummaryStride(0):directGroups*SummaryStride(0)+group*SummaryStride(1));}/** Stores only enough local winners to preserve the exact global fair/priority result. */
void StoreTop(int targetOffset,int quota,bool fair)
{
    int lane=int(gl_LocalInvocationIndex);
    if(lane<quota)
    {
        if(fair)scratch[targetOffset+lane]=keys[lane].z;
        else {int p=targetOffset+lane*3;scratch[p]=keys[lane].x;scratch[p+1]=keys[lane].y;scratch[p+2]=keys[lane].z;}
    }
    barrier();
}
/** Reduces disjoint candidate sets; a global top-K candidate must belong to its child's top K. */
void Candidates(bool initial)
{
    int lane=int(gl_LocalInvocationIndex);
    int oldDirect=workReduction.z,oldDiffuse=workReduction.w;
    int nextDirect=initial?oldDirect:(oldDirect+3)/4,nextDiffuse=initial?oldDiffuse:(oldDiffuse+3)/4;
    int kind=int(gl_WorkGroupID.x)<nextDirect?0:1;
    int group=int(gl_WorkGroupID.x)-(kind==0?0:nextDirect);
    int fairQuota=FairQuota(kind),priorityQuota=Budget(kind),stride=SummaryStride(kind);
    int sortSize=256;while(!initial&&sortSize<4*priorityQuota)sortSize*=2;
    int targetOffset=Summary(workReduction.y,kind,group,nextDirect);
    if(lane==0)pendingCount=0u;barrier();
    uint job=Invalid;uvec3 priority=uvec3(Invalid);
    if(initial)
    {
        int candidate=group*256+lane,total=workExtent.w*(kind==0?2:1);
        if(candidate<total&&Pending(candidate,kind)){job=uint(candidate);priority=PriorityKey(job,kind);atomicAdd(pendingCount,1u);}
    }
    else if(lane<4)
    {
        int child=group*4+lane,count=kind==0?oldDirect:oldDiffuse;
        if(child<count)atomicAdd(pendingCount,scratch[Summary(workReduction.x,kind,child,oldDirect)+stride-1]);
    }
    // Every lane observes the same completed count. Empty children have no possible global
    // winner, so publish the identical padded summary without running either sorting network.
    // Demand and ages stay in region metadata; later density/source changes can make them eligible.
    barrier();
    if(pendingCount==0u)
    {
        for(int i=lane;i<stride;i+=256)scratch[targetOffset+i]=i==stride-1?0u:Invalid;
        return;
    }
    for(int index=lane;index<sortSize;index+=256)
    {
        if(initial)keys[index]=FairKey(job,kind);
        else
        {
            int child=group*4+index/fairQuota,count=kind==0?oldDirect:oldDiffuse;
            keys[index]=index<4*fairQuota&&child<count?
                FairKey(scratch[Summary(workReduction.x,kind,child,oldDirect)+index%fairQuota],kind):uvec3(Invalid);
        }
    }
    Sort(sortSize);StoreTop(targetOffset,fairQuota,true);
    for(int index=lane;index<sortSize;index+=256)
    {
        if(initial)keys[index]=priority;
        else
        {
            int child=group*4+index/priorityQuota,count=kind==0?oldDirect:oldDiffuse;
            if(index<4*priorityQuota&&child<count)
            {int p=Summary(workReduction.x,kind,child,oldDirect)+fairQuota+(index%priorityQuota)*3;keys[index]=uvec3(scratch[p],scratch[p+1],scratch[p+2]);}
            else keys[index]=uvec3(Invalid);
        }
    }
    Sort(sortSize);StoreTop(targetOffset+fairQuota,priorityQuota,false);
    if(lane==0)scratch[targetOffset+stride-1]=pendingCount;
}
/** Emits bounded unique lists from global winners, retaining exactly half-budget round-robin service. */
void Emit(int kind,int groups)
{
    int list=kind==0?DirectJobs:DiffuseJobs,budget=Budget(kind),quota=FairQuota(kind),count=0;
    uint backlog=0u;
    if(groups!=0)
    {
        int sourceOffset=Summary(workReduction.x,kind,0,workReduction.z);
        backlog=scratch[sourceOffset+SummaryStride(kind)-1];
        for(int i=0;i<quota;i++)
        {
            uint job=scratch[sourceOffset+i];if(job==Invalid)break;
            scratch[list+count++]=job;scratch[18+kind]=(job+1u)%uint(workExtent.w*(kind==0?2:1));
        }
        for(int i=0;i<budget&&count<budget;i++)
        {
            uint job=scratch[sourceOffset+quota+i*3+2];if(job==Invalid)break;
            bool duplicate=false;for(int n=0;n<count;n++)duplicate=duplicate||scratch[list+n]==job;
            if(!duplicate)scratch[list+count++]=job;
        }
        for(int i=0;i<count;i++)
        {
            int job=int(scratch[list+i]),region=kind==0?job/2:job;
            regions[region*16]|=uint(kind==0?512<<(job%2):2048);
        }
    }
    scratch[16+kind]=uint(count);scratch[22+kind]=backlog;
    commands[kind*3]=uint(count);commands[kind*3+1]=kind==0?1u:uint(workSettings.w);commands[kind*3+2]=1u;
    if(kind==1)
    {
        // Keep full re-encoding and bounded previous-block repair independently indirect.
        uint width=uint(workExtent.x);
        commands[12]=count!=0?width:0u;commands[13]=17u;commands[14]=width;
        commands[15]=count!=0?scratch[38]:0u;commands[16]=1u;commands[17]=1u;
    }
    if(kind==0)
    {
        uint solar=0u,lunar=0u;
        for(int i=0;i<count;i++)
        {
            uint job=scratch[DirectJobs+i];
            if((job&1u)==0u)scratch[16768u+solar++]=job/2u;
            else scratch[17024u+lunar++]=job/2u;
        }
        scratch[29]=solar;scratch[30]=lunar;
        commands[6]=solar;commands[7]=uint(workSettings.z);commands[8]=1u;
        commands[9]=lunar;commands[10]=uint(workSettings.z);commands[11]=1u;
    }
}
/** Runs barrier-separated initialization, parallel merge, hierarchical selection or successful-only completion. */
void main()
{
    uint id=gl_GlobalInvocationID.x;
    #if VGE_VOLUME_WORK_OPERATION==0
    if(id!=0u)return;
    int dirty=workStamps.w;if(scratch[6]!=0u)dirty|=7;if(scratch[27]!=0u)dirty|=4;
    scratch[28]=uint(dirty);scratch[6]=0u;scratch[27]=0u;
    scratch[25]=workCell.w!=0? scratch[31]:0u;scratch[26]=0u;scratch[39]=0u;
    #elif VGE_VOLUME_WORK_OPERATION==1
    if(id>=uint(workExtent.w))return;
    int dirty=int(scratch[28]),b=int(id)*16;uint flags=regions[b]&~(512u|1024u|2048u);
    for(int s=0;s<2;s++)
    {
        if((dirty&(1<<s))!=0||((workStamps.z&(1<<s))!=0&&regions[b+6+s*2]==0u))
        {if((flags&2u)==0u)regions[b+9]=uint(workStamps.x);regions[b+5+s*2]=uint(workStamps.y);flags|=2u;}
        if((workSettings.x&(1<<s))!=0)regions[b+6+s*2]=0u;
    }
    if(workSettings.y!=0&&((dirty&4)!=0||(flags&4u)==0u||regions[b+13]==0u))
    {if((flags&4u)==0u)regions[b+14]=uint(workStamps.x);regions[b+12]=uint(workStamps.y);flags|=4u;}
    regions[b]=flags;
    #elif VGE_VOLUME_WORK_OPERATION==2
    Candidates(true);
    #elif VGE_VOLUME_WORK_OPERATION==3
    Candidates(false);
    #elif VGE_VOLUME_WORK_OPERATION==4
    if(id!=0u)return;Emit(0,workReduction.z);Emit(1,workReduction.w);
    #else
    if(id!=0u)return;
    uint mask=uint(workSlots.w);
    // A chained batch commits all channels together after every solver. A density failure
    // must reject lighting stamps too, even if a substituted zero density made those rays finite.
    scratch[26]=scratch[25];if(scratch[26]!=0u)return;
    if((mask&3u)!=0u)for(uint i=0u;i<scratch[16];i++)
    {
        int job=int(scratch[DirectJobs+int(i)]),r=job/2,s=job%2,b=r*16;
        if((mask&(1u<<uint(s)))==0u)continue;
        regions[b+6+s*2]=regions[b+5+s*2];regions[b+10+s]=uint(workStamps.x);
        regions[b]|=8192u;
        regions[b]&=~uint(512<<s);
        if(regions[b+5]==regions[b+6]&&regions[b+7]==regions[b+8])regions[b]&=~2u;
        scratch[27]=1u;
    }
    if((mask&4u)!=0u&&scratch[17]!=0u)
    {
        // Only successful nonempty work replaces the history needed to repair the next spare.
        scratch[38]=scratch[17];
        for(uint i=0u;i<scratch[17];i++)
        {
            uint region=scratch[DiffuseJobs+int(i)];scratch[17280u+i]=region;
            int b=int(region)*16;
            regions[b+13]=regions[b+12];regions[b+15]=uint(workStamps.x);regions[b]&=~(4u|2048u);regions[b]|=16384u;scratch[27]=1u;
        }
    }
    #endif
}
