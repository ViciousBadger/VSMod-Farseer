using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarseerClientConfig
{
    [ProtoMember(1)]
    public int FarViewDistance = 3000;
}

public class FarseerServerConfig
{
    [ProtoMember(1)]
    public int HeightmapGridSize = 64;
    [ProtoMember(2)]
    public float ChunkQueueThreshold = 0.5f;
}
