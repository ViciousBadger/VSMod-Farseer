
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
    public int FarViewDistance = 2048;
}

[ProtoContract]
public class FarRegionUnload
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
    public int RegionX;
    [ProtoMember(3)]
    public int RegionZ;
    [ProtoMember(4)]
    public int RegionSize; // total size in blocks
    [ProtoMember(5)]
    public FarRegionHeightmap Heightmap;
}

[ProtoContract]
public class FarRegionHeightmap
{
    [ProtoMember(1)]
    public int GridSize; // ..of each axis
    [ProtoMember(2)]
    public int[] Points;
}
