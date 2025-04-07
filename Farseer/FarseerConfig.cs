using ProtoBuf;

namespace Farseer;

[ProtoContract]
public class FarseerClientConfig
{
    [ProtoMember(1)]
    public int FarViewDistance;
    [ProtoMember(2)]
    public float SkyTint;
    [ProtoMember(3)]
    public float ColorTintR;
    [ProtoMember(4)]
    public float ColorTintG;
    [ProtoMember(5)]
    public float ColorTintB;
    [ProtoMember(6)]
    public float ColorTintA;
    [ProtoMember(7)]
    public float LightLevelBias;
    [ProtoMember(8)]
    public float FadeBias;

    public FarseerClientConfig()
    {
        Reset();
    }

    public void Reset()
    {
        FarViewDistance = 4096;
        SkyTint = 2.0f;
        ColorTintR = 0.26f;
        ColorTintG = 0.29f;
        ColorTintB = 0.45f;
        ColorTintA = 0.25f;
        LightLevelBias = 0.55f;
        FadeBias = 0.4f;
    }
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
