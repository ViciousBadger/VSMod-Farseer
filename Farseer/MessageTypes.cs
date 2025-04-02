
using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarChunkMessage
{
    [ProtoMember(1)]
    public int ChunkPosX;
    [ProtoMember(2)]
    public int ChunkPosZ;
    [ProtoMember(3)]
    public int[] Heightmap;
}

[ProtoContract]
public class FarEnableRequest
{
    [ProtoMember(1)]
    public int DesiredRenderDistance;
}

[ProtoContract]
public class FarRegionRequest
{
    [ProtoMember(1)]
    public long RegionIndex;
}

[ProtoContract]
public class FarRegionData
{
    [ProtoMember(1)]
    public long RegionIndex;
    [ProtoMember(2)]
    public int[] Heightmap;
}
