using ProtoBuf;

namespace Farseer;

public class FarseerClientConfig
{
    public int FarViewDistance;
    public float SkyTint;
    public float ColorTintR;
    public float ColorTintG;
    public float ColorTintB;
    public float ColorTintA;
    public float LightLevelBias;
    public float FadeBias;

    public FarseerClientConfig()
    {
        Reset();
    }

    public void Reset()
    {
        FarViewDistance = 4096;
        SkyTint = 5.0f;
        ColorTintR = 0.26f;
        ColorTintG = 0.29f;
        ColorTintB = 0.45f;
        ColorTintA = 0.40f;
        LightLevelBias = 0.70f;
        FadeBias = 0.4f;
    }

    public FarseerClientConfig Clone()
    {
        return new FarseerClientConfig()
        {
            FarViewDistance = FarViewDistance,
            SkyTint = SkyTint,
            ColorTintR = ColorTintR,
            ColorTintG = ColorTintG,
            ColorTintB = ColorTintB,
            ColorTintA = ColorTintA,
            LightLevelBias = LightLevelBias,
            FadeBias = FadeBias
        };
    }

    public bool ShouldShareWithServer(FarseerClientConfig before)
    {
        return FarViewDistance != before.FarViewDistance;
    }

    public FarseerServerPlayerConfig ToServerPlayerConfig()
    {
        return new FarseerServerPlayerConfig()
        {
            FarViewDistance = FarViewDistance
        };
    }
}

[ProtoContract]
public class FarseerServerPlayerConfig
{
    [ProtoMember(1)]
    public int FarViewDistance;
}

public class FarseerServerConfig
{
    public int HeightmapGridSize = 128;
    public int MaxClientViewDistance = 4096;
    public int ChunkGenQueueThreshold = 64;
}
