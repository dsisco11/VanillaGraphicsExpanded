/// Copies the complete original domain, including the final partial workgroup.
void main(){
    uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
    // Ordered policies execute their original serial traversal once and publish only a bounded scalar proof.
    if(mode==30) {
        if(index!=0u)return;
        vec4 value=project(vec4(texelFetch(sourceImage,ivec3(0),0)),vec4(0),ivec3(0),0u);
        imageStore(destinationImage,ivec3(0),vec4(value.r,uintBitsToFloat(0x47505541u),
            float(orderedVisits&65535u),float(orderedVisits>>16)));
        return;
    }
    if(index>=uint(extent.x*extent.y*extent.z))return;
    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
    imageStore(destinationImage,p,project(vec4(texelFetch(sourceImage,p,0)),((constantValue&2u)!=0u ? vec4(texelFetch(referenceImage,p,0)) : vec4(texelFetch(sourceImage,p,0))),p,index));
}
