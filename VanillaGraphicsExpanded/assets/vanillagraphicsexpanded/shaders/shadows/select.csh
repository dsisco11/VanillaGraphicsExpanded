#version 450 core

// A single bounded producer owns the batch certificate. Selection never escapes to
// CPU readback; no command becomes active unless every required association succeeds.
layout(local_size_x=1, local_size_y=1, local_size_z=1) in;
layout(std430, binding=0) readonly buffer SelectionInputs { uint inputs[]; };
layout(std430, binding=1) readonly buffer SelectionWorld { uvec4 world[]; };
layout(std430, binding=2) readonly buffer SelectionHeaders { uint headers[]; };
layout(std430, binding=3) readonly buffer SelectionCoordinates { ivec4 coordinates[]; };
layout(std430, binding=4) readonly buffer SelectionRanges { uint ranges[]; };
layout(std430, binding=5) buffer SelectionOutput { uint outputWords[]; };

uint visits, pairs, instances, triangles, samples, failure;

/// Charges all candidate, native-range and lookup visits, including duplicates and rejected inputs.
bool visit() {
    if(visits>=inputs[7]) { failure=2u; return false; }
    visits++; return true;
}

/// Matches the native coordinate table's unsigned wrapping hash.
uint hashCoordinate(ivec4 c) {
    uint h=2166136261u;
    h=(h^uint(c.w))*16777619u; h=(h^uint(c.x))*16777619u;
    h=(h^uint(c.y))*16777619u; return (h^uint(c.z))*16777619u;
}

/// Looks up a fixed chunk partition, retaining unresolved cells as failure rather than empty geometry.
bool findChunk(ivec4 coordinate,out uint index) {
    uint hash=hashCoordinate(coordinate);
    for(uint probe=0u;probe<inputs[5];probe++) {
        if(!visit()) return false;
        index=((hash+probe)&inputs[4])*4u;
        uvec4 flags=world[index+2u];
        if(flags.z==0u) break;
        if(all(equal(ivec4(world[index]),coordinate))) {
            if((flags.z&3u)==3u) return true;
            break;
        }
    }
    failure=3u; return false;
}

/// Rejects only a complete fixed partition outside one homogeneous XY clipping plane.
bool intersects(ivec4 coordinate,uint view) {
    uint v=32u+view*64u;
    // Whole-column construction avoids a driver SPIR-V ingestion defect for
    // dynamically indexed writes to double-precision matrix components.
    dmat4 matrix=dmat4(mat4(
        uintBitsToFloat(uvec4(inputs[v],inputs[v+1u],inputs[v+2u],inputs[v+3u])),
        uintBitsToFloat(uvec4(inputs[v+4u],inputs[v+5u],inputs[v+6u],inputs[v+7u])),
        uintBitsToFloat(uvec4(inputs[v+8u],inputs[v+9u],inputs[v+10u],inputs[v+11u])),
        uintBitsToFloat(uvec4(inputs[v+12u],inputs[v+13u],inputs[v+14u],inputs[v+15u]))));
    dvec3 origin=dvec3(packDouble2x32(uvec2(inputs[v+44u],inputs[v+45u])),
        packDouble2x32(uvec2(inputs[v+46u],inputs[v+47u])),
        packDouble2x32(uvec2(inputs[v+48u],inputs[v+49u])));
    dvec2 size=dvec2(inputs[v+18u],inputs[v+19u]);
    dvec4 rectangle=dvec4(inputs[v+20u],inputs[v+21u],inputs[v+22u],inputs[v+23u]);
    rectangle=rectangle/dvec4(size,size)*2.0lf-1.0lf;
    bvec4 outside=bvec4(true);
    double padding=double(uintBitsToFloat(inputs[19]));
    for(uint corner=0u;corner<8u;corner++) {
        dvec3 p=dvec3(coordinate.xyz)*32.0lf-origin+
            dvec3((corner&1u)!=0u?32.0lf+padding:-padding,(corner&2u)!=0u?32.0lf+padding:-padding,
                (corner&4u)!=0u?32.0lf+padding:-padding);
        if(coordinate.w!=1) p.y+=double(coordinate.w)*32768.0lf;
        dvec4 clip=matrix*dvec4(p,1.0lf);
        // Scale the forward error by the projection coefficients as well as position:
        // fine virtual texels can amplify even a small float position rounding error.
        dvec4 magnitude=abs(matrix[0])*(abs(p.x)+64.0lf)+abs(matrix[1])*(abs(p.y)+64.0lf)+
            abs(matrix[2])*(abs(p.z)+64.0lf)+abs(matrix[3]);
        dvec4 error=(dvec4(magnitude.xy,magnitude.xy)+abs(rectangle)*magnitude.w+1.0lf)*1.0e-5lf;
        dvec4 planes=dvec4(clip.x-rectangle.x*clip.w,clip.y-rectangle.y*clip.w,
            rectangle.z*clip.w-clip.x,rectangle.w*clip.w-clip.y);
        outside=bvec4(outside.x&&planes.x < -error.x,outside.y&&planes.y < -error.y,
            outside.z&&planes.z < -error.z,outside.w&&planes.w < -error.w);
    }
    return !any(outside);
}

/// Resolves one current range into its CPU-established interval, deduplicating by exact range/view identity.
bool selectRange(uint range,uint view) {
    if(!visit()) return false;
    if(range>=inputs[3]) { failure=3u; return false; }
    uint r=range*11u;
    if(ranges[r+2u]==0u || ranges[r+5u]==0u) return true;
    uint command=ranges[r+1u];
    if(command>=inputs[2]) { failure=3u; return false; }
    uint slot=view*inputs[2]+command, address=16u+slot*5u;
    if(outputWords[address]!=0u) return true;
    uint cost=ranges[r+3u], v=32u+view*64u;
    uint pixels=inputs[v+18u]*inputs[v+19u];
    // Division guards every multiplication before it can wrap. Each triangle is
    // conservatively allowed to cover the entire admitted raster rectangle.
    if(pairs>=inputs[8] || instances>=inputs[16] || cost>inputs[9]-triangles ||
        (cost!=0u && pixels>(inputs[10]-samples)/cost)) { failure=4u; return false; }
    pairs++; instances++; triangles+=cost; samples+=cost*pixels;
    outputWords[address]=ranges[r+5u];
    outputWords[address+1u]=1u;
    outputWords[address+2u]=ranges[r+4u];
    outputWords[address+3u]=ranges[r+6u];
    outputWords[address+4u]=slot;
    uint selector=16u+inputs[11]*5u+slot*2u;
    outputWords[selector]=range; outputWords[selector+1u]=view;
    return true;
}

/// Selects a complete owner-supplied draw conservatively when its deformed extents are not certified.
bool selectObject(uint objectIndex,uint view) {
    if(!visit()) return false;
    uint r=inputs[15]+objectIndex*8u;
    uint slot=view*inputs[2]+inputs[r], address=16u+slot*5u;
    uint count=inputs[r+4u], cost=inputs[r+5u], v=32u+view*64u;
    // Native object dimensions are semantic scene identity, not merely a large
    // coordinate offset that might happen to lie outside this light's projection.
    if(inputs[r+7u]!=inputs[v+26u]) return true;
    uint pixels=inputs[v+18u]*inputs[v+19u];
    if(pairs>=inputs[8] || count>inputs[16]-instances || cost>inputs[9]-triangles ||
        (cost!=0u && pixels>(inputs[10]-samples)/cost)) { failure=4u; return false; }
    pairs++; instances+=count; triangles+=cost; samples+=cost*pixels;
    outputWords[address]=inputs[r+2u]; outputWords[address+1u]=count;
    outputWords[address+2u]=inputs[r+1u]; outputWords[address+3u]=inputs[r+3u];
    // The adapter binds this slot's selector separately. Base instance zero keeps
    // every original native instance stream and gl_InstanceID unchanged.
    outputWords[address+4u]=0u;
    uint selector=16u+inputs[11]*5u+slot*2u;
    outputWords[selector]=inputs[r+6u]; outputWords[selector+1u]=view;
    return true;
}

/// Checks one exact completed dependency certificate without reading it back to the CPU.
bool completeCandidate(uint a,uint slot,uvec2 generation) {
    uint h=slot*32u;
    return headers[h]==3u && headers[h+16u]==0u && headers[h+1u]<=inputs[6] &&
        all(equal(uvec2(headers[h+24u],headers[h+25u]),generation)) &&
        all(equal(uvec2(headers[h+21u],headers[h+22u]),uvec2(inputs[a+4u],inputs[a+5u]))) &&
        all(equal(uvec2(headers[h+28u],headers[h+29u]),uvec2(inputs[a+6u],inputs[a+7u]))) &&
        all(equal(uvec2(headers[h+19u],headers[h+20u]),uvec2(inputs[a+8u],inputs[a+9u])));
}

/// Prefers a complete guarded replacement and otherwise retains complete covered old candidates.
bool selectAssociation(uint association) {
    if(!visit()) return false;
    uint a=32u+inputs[0]*64u+association*16u;
    uint view=inputs[a], slot=inputs[a+1u];
    bool replacement=false;
    if(inputs[a+10u]!=0u) {
        if(!visit()) return false;
        uint next=inputs[a+10u]-1u;
        if(completeCandidate(a,next,uvec2(inputs[a+11u],inputs[a+12u]))) {slot=next;replacement=true;}
    }
    uint h=slot*32u;
    if(!replacement && !completeCandidate(a,slot,uvec2(inputs[a+2u],inputs[a+3u]))) {
        failure=1u; return false;
    }
    for(uint c=0u;c<headers[h+1u];c++) {
        if(!visit()) return false;
        ivec4 coordinate=coordinates[slot*inputs[6]+c];
        uint chunk;
        // Validate unresolved cells even when raster rejection would hide them.
        if(!findChunk(coordinate,chunk)) return false;
        if(!intersects(coordinate,view)) continue;
        uvec4 span=world[chunk+3u];
        if(span.x>inputs[3] || span.y>inputs[3]-span.x) { failure=3u; return false; }
        for(uint r=0u;r<span.y;r++) if(!selectRange(span.x+r,view)) return false;
    }
    return true;
}

/// Initializes every submitted slot and publishes active commands only after whole-batch success.
void main() {
    for(uint i=0u;i<16u+inputs[11]*7u;i++) outputWords[i]=0u;
    visits=0u; pairs=0u; instances=0u; triangles=0u; samples=0u; failure=0u;
    for(uint a=0u;a<inputs[1];a++) if(!selectAssociation(a)) break;
    // These range references come from the same bounded native-table traversal;
    // no persistent classification or full static-range/view product is required.
    for(uint view=0u;view<inputs[0] && failure==0u;view++) {
        for(uint i=0u;i<inputs[17];i++) if(!selectRange(inputs[inputs[18]+i],view)) break;
    }
    for(uint view=0u;view<inputs[0] && failure==0u;view++)
        for(uint objectIndex=0u;objectIndex<inputs[14];objectIndex++) if(!selectObject(objectIndex,view)) break;
    for(uint slot=0u;slot<inputs[11];slot++) {
        uint address=16u+slot*5u;
        if(failure!=0u) { outputWords[address]=0u; outputWords[address+1u]=0u; }
    }
    outputWords[0]=failure==0u?1u:0u; outputWords[1]=failure;
    outputWords[2]=visits; outputWords[3]=pairs; outputWords[4]=triangles; outputWords[5]=samples;
    outputWords[6]=instances;
    outputWords[12]=outputWords[0]; outputWords[13]=inputs[12]; outputWords[14]=inputs[13];
}
