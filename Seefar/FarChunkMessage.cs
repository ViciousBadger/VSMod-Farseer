
using ProtoBuf;

namespace Seefar;

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
