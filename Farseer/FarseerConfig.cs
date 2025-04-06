using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarseerClientConfig
{
    [ProtoMember(1)]
    public int FarViewDistance = 3000;
    [ProtoMember(2)]
    public float SkyTint = 1.0f;
}

public class FarseerServerConfig
{
    [ProtoMember(1)]
    public int HeightmapGridSize = 128;
    [ProtoMember(2)]
    public float ChunkQueueThreshold = 0.25f;
    [ProtoMember(3)]
    public int MaxClientViewDistance = 3000;
}
