using ProtoBuf;

namespace Farseer;

public class FarseerClientConfig
{
    public bool Enabled;
    public int FarViewDistance;
    public float SkyTint;
    public float ColorTintR;
    public float ColorTintG;
    public float ColorTintB;
    public float ColorTintA;
    public float LightLevelBias;
    public float FadeBias;
    public float GlobeEffect;

    public FarseerClientConfig()
    {
        Reset();
    }

    public void Reset()
    {
        Enabled = true;
        FarViewDistance = 4096;
        SkyTint = 5.0f;
        ColorTintR = 0.26f;
        ColorTintG = 0.29f;
        ColorTintB = 0.45f;
        ColorTintA = 0.40f;
        LightLevelBias = 0.70f;
        FadeBias = 0.4f;
        GlobeEffect = 0.05f;
    }

    public FarseerClientConfig Clone()
    {
        return (FarseerClientConfig)this.MemberwiseClone();
    }

    public bool ShouldShareWithServer(FarseerClientConfig before)
    {
        return FarViewDistance != before.FarViewDistance || Enabled != before.Enabled;
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
    public bool GenRealChunks = false;
    public bool DisableProgressLogging = false;
}
