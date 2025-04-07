using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarseerClientConfig
{
    [ProtoMember(1)]
    public int FarViewDistance = 3072;
    [ProtoMember(2)]
    public float SkyTint = 1.5f;
    [ProtoMember(3)]
    public float ColorTintR = 0.06f;
    [ProtoMember(4)]
    public float ColorTintG = 0.08f;
    [ProtoMember(5)]
    public float ColorTintB = 0.2f;
    [ProtoMember(6)]
    public float ColorTintA = 0.15f;
    [ProtoMember(7)]
    public float LightLevelBias = 0.5f;
    [ProtoMember(8)]
    public float FadeBias = 0.5f;
}

public class FarseerServerConfig
{
    [ProtoMember(1)]
    public int HeightmapGridSize = 128;
    [ProtoMember(2)]
    public int ChunkGenerationQueueThreshold = 512;
    [ProtoMember(3)]
    public int MaxClientViewDistance = 3000;
}
