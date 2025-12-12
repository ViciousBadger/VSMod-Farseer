using ProtoBuf;
using Vintagestory.API.MathTools;

namespace Farseer;

[ProtoContract]
public class FarseerEnable
{
    [ProtoMember(1)]
    public FarseerServerPlayerConfig PlayerConfig;
    [ProtoMember(2)]
    public bool SupportsCompressedFarRegions;
}

[ProtoContract]
public class FarseerDisable
{ }

[ProtoContract]
public class FarRegionUnload
{
    [ProtoMember(1)]
    public long[] RegionIndices;
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
    [ProtoMember(7)]
    public byte[] CompressedPoints; // Optional compressed points
    [ProtoMember(8)]
    public byte[] CompressedColors; // Optional compressed colors
    [ProtoMember(9)]
    public bool Compressed;

    /// <summary>
    /// Calculate the center position in world space of this region (With optional height)
    /// </summary>
    public Vec3d GetCenterPos(float y = 0)
    {
        return new Vec3d(
            RegionX * RegionSize + RegionSize / 2.0,
            y,
            RegionZ * RegionSize + RegionSize / 2.0
        );
    }
}

[ProtoContract]
public class FarRegionHeightmap
{
    [ProtoMember(1)]
    public int GridSize; // ..of each axis
    [ProtoMember(2)]
    public int[] Points;
    [ProtoMember(3)]
    public int[] Colors; // Optional: RGB color per grid point (R=climate temp, G=climate rain, B=forest/beach)
}
