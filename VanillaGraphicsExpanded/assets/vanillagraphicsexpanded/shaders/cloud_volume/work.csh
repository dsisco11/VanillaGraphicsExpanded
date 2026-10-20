#version 430 core
layout(local_size_x=1) in;
layout(std140) uniform VolumeWork
{
    ivec4 workExtent;
    ivec4 workStamps; // frame, revision, active sources, dirty sources/diffuse
    ivec4 workSlots;
    vec4 workFirst;
    vec4 workCell;
    ivec4 workSettings; // newly active, diffuse enabled
    ivec4 workReserved;
    vec4 workPlanes[6];
};
layout(std430) buffer WorkRegions {uint regions[];};
layout(std430) buffer WorkScratch {uint scratch[];};
layout(std430) buffer WorkCommands {uint commands[];};
const int DirectJobs=16384,DiffuseJobs=16448,Heap=16512;

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
/** Tests per-source targets independently; one completed light never consumes another light's pending work. */
bool Pending(int job,int kind)
{
    int region=kind==0?job/2:job;
    if(kind==1)return workSettings.y!=0&&(regions[region*16]&4u)!=0u;
    int source=job%2;
    return (workStamps.z&(1<<source))!=0 && (regions[region*16+5+source*2]!=regions[region*16+6+source*2]||regions[region*16+6+source*2]==0u);
}
/** Separates incompatible/new coverage, activation, visible stale work and background work. */
int Priority(int job,int kind)
{
    int region=kind==0?job/2:job;
    uint complete=regions[region*16+(kind==0?6+(job%2)*2:13)];
    if(complete==0u)return 0;
    if(kind==0&&(workSettings.x&(1<<(job%2)))!=0)return 1;
    return Visible(region)?2:3;
}
/** Compares priority, then persistent waiting age, then stable job index. */
bool Better(uint a,uint b,int kind)
{
    int pa=Priority(int(a),kind),pb=Priority(int(b),kind);if(pa!=pb)return pa<pb;
    int ra=kind==0?int(a)/2:int(a),rb=kind==0?int(b)/2:int(b),word=kind==0?9:14;
    uint aa=uint(workStamps.x)-regions[ra*16+word],bb=uint(workStamps.x)-regions[rb*16+word];
    return aa>bb||(aa==bb&&a<b);
}
/** Maintains a small worst-first priority heap in existing scratch rather than rescanning per emitted job. */
void Offer(uint job,inout int count,int limit,int kind)
{
    if(count<limit)
    {
        int i=count++;scratch[Heap+i]=job;
        while(i>0){int p=(i-1)/2;if(!Better(scratch[Heap+p],scratch[Heap+i],kind))break;
            uint t=scratch[Heap+p];scratch[Heap+p]=scratch[Heap+i];scratch[Heap+i]=t;i=p;}
    }
    else if(Better(job,scratch[Heap],kind))
    {
        scratch[Heap]=job;int i=0;
        while(i*2+1<count)
        {
            int c=i*2+1;if(c+1<count&&Better(scratch[Heap+c],scratch[Heap+c+1],kind))c++;
            if(!Better(scratch[Heap+i],scratch[Heap+c],kind))break;
            uint t=scratch[Heap+i];scratch[Heap+i]=scratch[Heap+c];scratch[Heap+c]=t;i=c;
        }
    }
}
/** Emits separate lists with half guaranteed round-robin service and half oldest priority service. */
void Select(int kind,int budget,int list,int countWord,int cursorWord)
{
    int total=workExtent.w*(kind==0?2:1),count=0,cursor=int(scratch[cursorWord]%uint(total)),backlog=0;
    for(int n=0;n<total&&count<budget/2;n++)
    {
        int job=(cursor+n)%total;if(!Pending(job,kind))continue;
        scratch[list+count++]=uint(job);scratch[cursorWord]=uint((job+1)%total);
        int region=kind==0?job/2:job;regions[region*16]|=uint(kind==0?512<<(job%2):2048);
    }
    int heapCount=0;
    for(int job=0;job<total;job++)if(Pending(job,kind))
    {
        backlog++;int region=kind==0?job/2:job;uint marker=uint(kind==0?512<<(job%2):2048);
        if((regions[region*16]&marker)==0u)Offer(uint(job),heapCount,budget-count,kind);
    }
    for(int i=0;i<heapCount;i++){uint job=scratch[Heap+i];scratch[list+count++]=job;
        int region=kind==0?int(job)/2:int(job);regions[region*16]|=uint(kind==0?512<<(int(job)%2):2048);}
    scratch[countWord]=uint(count);scratch[22+kind]=uint(backlog);
    commands[kind*3]=uint(count);commands[kind*3+1]=1u;commands[kind*3+2]=1u;
}
/** Selects work without claiming completion, or commits only explicitly successful channels. */
void main()
{
    #if VGE_VOLUME_WORK_OPERATION==0
    int dirty=workStamps.w;
    if(scratch[6]!=0u)dirty|=7;
    if(scratch[27]!=0u)dirty|=4;
    for(int r=0;r<workExtent.w;r++)
    {
        int b=r*16;uint flags=regions[b]&~(512u|1024u|2048u);
        for(int s=0;s<2;s++)
        {
            if((dirty&(1<<s))!=0 || ((workStamps.z&(1<<s))!=0&&regions[b+6+s*2]==0u))
            {
                if((flags&2u)==0u)regions[b+9]=uint(workStamps.x);
                regions[b+5+s*2]=uint(workStamps.y);flags|=2u;
            }
            if((workSettings.x&(1<<s))!=0)regions[b+6+s*2]=0u;
        }
        if(workSettings.y!=0&&((dirty&4)!=0||(flags&4u)==0u||regions[b+13]==0u))
        {
            if((flags&4u)==0u)regions[b+14]=uint(workStamps.x);
            regions[b+12]=uint(workStamps.y);flags|=4u;
        }
        regions[b]=flags;
    }
    scratch[6]=0u;scratch[27]=0u;scratch[25]=0u;scratch[26]=0u;
    Select(0,64,DirectJobs,16,18);Select(1,32,DiffuseJobs,17,19);
    #else
    uint mask=uint(workSlots.w);
    scratch[26]=scratch[25]&mask;if(scratch[26]!=0u)return;
    if((mask&3u)!=0u)for(uint i=0u;i<scratch[16];i++)
    {
        int job=int(scratch[DirectJobs+int(i)]),r=job/2,s=job%2,b=r*16;
        if((mask&(1u<<uint(s)))==0u)continue;
        regions[b+6+s*2]=regions[b+5+s*2];regions[b+10+s]=uint(workStamps.x);
        regions[b]&=~uint(512<<s);
        if(regions[b+5]==regions[b+6]&&regions[b+7]==regions[b+8])regions[b]&=~2u;
        scratch[27]=1u;
    }
    if((mask&4u)!=0u)for(uint i=0u;i<scratch[17];i++)
    {
        int b=int(scratch[DiffuseJobs+int(i)])*16;
        regions[b+13]=regions[b+12];regions[b+15]=uint(workStamps.x);regions[b]&=~(4u|2048u);
        scratch[27]=1u;
    }
    #endif
}
