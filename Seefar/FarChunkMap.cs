using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Seefar;

public class FarChunkMap
{

    private Dictionary<Vec2i, FarChunk> loadedChunks = new Dictionary<Vec2i, FarChunk>();

    public event Action<Vec2i, FarChunk> NewChunkLoaded;

    public FarChunkMap()
    {
    }

    public void Load(Vec2i coord, IWorldChunk chunk)
    {
        if (loadedChunks.ContainsKey(coord)) return;

        int[] heightmap = new int[4];
        heightmap[0] = chunk.MapChunk.WorldGenTerrainHeightMap[0];
        heightmap[1] = chunk.MapChunk.WorldGenTerrainHeightMap[31];
        heightmap[2] = chunk.MapChunk.WorldGenTerrainHeightMap[992];
        heightmap[3] = chunk.MapChunk.WorldGenTerrainHeightMap[1023];

        var farchunk = new FarChunk(heightmap);
        loadedChunks.Add(coord, farchunk);

        NewChunkLoaded?.Invoke(coord, farchunk);

        SeefarModSystem.Logger.Notification("[{0}]", string.Join(", ", heightmap));
    }
}
