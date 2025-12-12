using System;
using ProtoBuf;

namespace Farseer;

public class FarseerClientConfig
{
    private int _farViewDistance = 4096;
    private int _minDrawDistance = 0;

    public bool Enabled;

    public int FarViewDistance
    {
        get => _farViewDistance;
        set => _farViewDistance = Math.Clamp(value, 512, 16384);
    }

    public int MinDrawDistance
    {
        get => _minDrawDistance;
        set => _minDrawDistance = Math.Clamp(value, 0, 2048);
    }

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
        MinDrawDistance = 0;
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

public enum FarseerQuality
{
    Performance = 64,   // Fast, low detail, 1/4 data
    Balanced = 128,     // Default, good balance
    Quality = 256,      // High detail, 4x data
    Ultra = 512         // Maximum accuracy, 16x data
}

public class FarseerServerConfig
{
    private int _heightmapGridSize = 128;
    private int _maxClientViewDistance = 4096;
    private int _chunkGenQueueThreshold = 64;
    private int _maxBatchPeekColumns = 96;
    private int _lod1StartRegions = 12;
    private int _lod2StartRegions = 24;
    private int _minHeightmapGridSize = 32;
    private int _compressionThresholdBytes = 1024;
    private int _adaptivePeekMin = 8;
    private int _adaptivePeekMax = 64;

    public int HeightmapGridSize
    {
        get => _heightmapGridSize;
        set => _heightmapGridSize = Math.Clamp(value, 32, 512);
    }

    public int MaxClientViewDistance
    {
        get => _maxClientViewDistance;
        set => _maxClientViewDistance = Math.Clamp(value, 512, 16384);
    }

    public int ChunkGenQueueThreshold
    {
        get => _chunkGenQueueThreshold;
        set => _chunkGenQueueThreshold = Math.Clamp(value, 16, 2000);
    }

    public int MaxBatchPeekColumns
    {
        get => _maxBatchPeekColumns;
        set => _maxBatchPeekColumns = Math.Clamp(value, 16, 512);
    }

    public int Lod1StartRegions
    {
        get => _lod1StartRegions;
        set => _lod1StartRegions = Math.Clamp(value, 1, 256);
    }

    public int Lod2StartRegions
    {
        get => _lod2StartRegions;
        set => _lod2StartRegions = Math.Clamp(value, 1, 512);
    }

    public int MinHeightmapGridSize
    {
        get => _minHeightmapGridSize;
        set => _minHeightmapGridSize = Math.Clamp(value, 16, 256);
    }

    public int CompressionThresholdBytes
    {
        get => _compressionThresholdBytes;
        set => _compressionThresholdBytes = Math.Clamp(value, 256, 1_000_000);
    }

    public int AdaptivePeekMin
    {
        get => _adaptivePeekMin;
        set => _adaptivePeekMin = Math.Clamp(value, 4, 256);
    }

    public int AdaptivePeekMax
    {
        get => _adaptivePeekMax;
        set => _adaptivePeekMax = Math.Clamp(value, 8, 512);
    }

    public bool GenRealChunks = false;
    public bool DisableProgressLogging = false;
    public bool StoreBiomeData = true; // Store biome colors for more accurate terrain coloring
    public bool EnableBatchPeek = true;
    public bool EnableDistanceLod = true;
    public bool EnableCompression = true;

    // Quality preset helper
    public void SetQuality(FarseerQuality quality)
    {
        HeightmapGridSize = (int)quality;
    }

    public FarseerQuality GetQuality()
    {
        return HeightmapGridSize switch
        {
            64 => FarseerQuality.Performance,
            128 => FarseerQuality.Balanced,
            256 => FarseerQuality.Quality,
            512 => FarseerQuality.Ultra,
            _ => FarseerQuality.Balanced
        };
    }
}
