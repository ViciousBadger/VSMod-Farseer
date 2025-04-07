using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarseerClientConfig
{
    [ProtoMember(1)]
    public int FarViewDistance = 4096;
    [ProtoMember(2)]
    public float SkyTint = 2.0f;
    [ProtoMember(3)]
    public float ColorTintR = 0.26f;
    [ProtoMember(4)]
    public float ColorTintG = 0.29f;
    [ProtoMember(5)]
    public float ColorTintB = 0.45f;
    [ProtoMember(6)]
    public float ColorTintA = 0.25f;
    [ProtoMember(7)]
    public float LightLevelBias = 0.55f;
    [ProtoMember(8)]
    public float FadeBias = 0.4f;
}

public class FarseerServerConfig
{
    [ProtoMember(1)]
    public int HeightmapGridSize = 128;
    [ProtoMember(2)]
    public int ChunkGenerationQueueThreshold = 512;
    [ProtoMember(3)]
    public int MaxClientViewDistance = 4096;
}
