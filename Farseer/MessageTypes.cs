using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarEnableRequest
{
    [ProtoMember(1)]
    public FarseerClientConfig ClientConfig;
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
    public int RegionMapSize; // size of each axis of the region map, given to client for indexing.
    [ProtoMember(6)]
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
