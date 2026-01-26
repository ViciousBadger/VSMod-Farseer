using System;
using System.Buffers;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Farseer.Client;

public class FarRegionRenderer : IRenderer
{
    public struct PerModelData
    {
        public FarRegionData SourceData { get; set; }
        public Vec3d Position { get; set; }
        public MeshRef MeshRef { get; set; }
    }

    public double RenderOrder => 0.36;

    public int RenderRange => 9999;

    private readonly FarseerModSystem modSystem;
    private readonly ICoreClientAPI capi;
    private readonly Dictionary<long, PerModelData> activeRegionModels = [];
    private readonly HashSet<long> dirtyRegions = [];

    private readonly Matrixf modelMat = new();
    private IShaderProgram prog;

    private int farViewDistance = 3072;

    public FarRegionRenderer(FarseerModSystem modSystem, ICoreClientAPI capi)
    {
        this.modSystem = modSystem;
        this.capi = capi;

        capi.Event.ReloadShader += LoadShader;
        LoadShader();

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);
    }

    public void Init()
    {

        farViewDistance = modSystem.Client.Config.FarViewDistance;
        if (!capi.IsSinglePlayer)
        {
            // Limit to max server view distance
            farViewDistance = GameMath.Min(farViewDistance, capi.World.Config.GetInt("maxFarViewDistance"));
        }

        var clientMain = (ClientMain)capi.World;
        var mainCam = clientMain.MainCamera;
        var newZFar = GameMath.Max(3000, farViewDistance);
        mainCam.ZFar = newZFar;

        capi.Render.Reset3DProjection();
    }

    public bool LoadShader()
    {
        prog = capi.Shader.NewShaderProgram();

        prog.AssetDomain = "farseer";
        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        capi.Shader.RegisterFileShaderProgram("region", prog);

        var result = prog.Compile();

        return result;
    }

    private static long RegionNeighbourIndex(long idx, int offsetX, int offsetZ, int regionMapSize)
    {
        int rX = (int)(idx % regionMapSize);
        int rZ = (int)(idx / regionMapSize);

        return (long)(rZ + offsetZ) * regionMapSize + (rX + offsetX);
    }

    public void BuildRegion(FarRegionData sourceData, bool isRebuild = false)
    {
        static bool GridSizesMatch(FarRegionData regionA, FarRegionData regionB)
        {
            return regionA.Heightmap.GridSize == regionB.Heightmap.GridSize;
        }

        // Find neighbour id's for stitching
        var eastIdx = RegionNeighbourIndex(sourceData.RegionIndex, 1, 0, sourceData.RegionMapSize);
        var southIdx = RegionNeighbourIndex(sourceData.RegionIndex, 0, 1, sourceData.RegionMapSize);
        var southEastIdx = RegionNeighbourIndex(sourceData.RegionIndex, 1, 1, sourceData.RegionMapSize);

        var mesh = new MeshData(false);

        var gridSize = sourceData.Heightmap.GridSize;
        float cellSize = sourceData.RegionSize / (float)gridSize;

        var vertexCount = (gridSize + 1) * (gridSize + 1);
        var xyzLength = vertexCount * 3;
        mesh.SetVerticesCount(vertexCount);

        var indicesCount = gridSize * gridSize * 6;
        mesh.SetIndicesCount(indicesCount);

        // Rent arrays from pool to avoid GC pressure (~600KB per mesh)
        var xyzArray = ArrayPool<float>.Shared.Rent(xyzLength);
        var indicesArray = ArrayPool<int>.Shared.Rent(indicesCount);
        mesh.xyz = xyzArray;
        mesh.Indices = indicesArray;

        int xyz = 0;
        for (int vZ = 0; vZ <= gridSize; vZ++)
        {
            for (int vX = 0; vX <= gridSize; vX++)
            {
                mesh.xyz[xyz++] = vX * cellSize;

                int sample;

                if (vX == gridSize && vZ == gridSize && activeRegionModels.TryGetValue(southEastIdx, out PerModelData southEastData) && GridSizesMatch(sourceData, southEastData.SourceData))
                {
                    // For corner, select north-western-most point south-east neighbour 
                    sample = southEastData.SourceData.Heightmap.Points[0];
                }
                else if (vX == gridSize && vZ < gridSize && activeRegionModels.TryGetValue(eastIdx, out PerModelData eastData) && GridSizesMatch(sourceData, eastData.SourceData))
                {
                    // For x end, select west-most point of east neighbour
                    sample = eastData.SourceData.Heightmap.Points[vZ * gridSize];
                }
                else if (vZ == gridSize && vX < gridSize && activeRegionModels.TryGetValue(southIdx, out PerModelData southData) && GridSizesMatch(sourceData, southData.SourceData))
                {
                    // For z end, select north-most point of south neighbour
                    sample = southData.SourceData.Heightmap.Points[vX];
                }
                else
                {
                    // If no neighbour was sampled, use source data, but clamp to heightmap size.
                    int vXLimited = Math.Min(vX, gridSize - 1);
                    int vZLimited = Math.Min(vZ, gridSize - 1);
                    sample = sourceData.Heightmap.Points[vZLimited * gridSize + vXLimited];
                }

                mesh.xyz[xyz++] = sample;
                mesh.xyz[xyz++] = vZ * cellSize;
            }
        }

        int index = 0;
        for (int i = 0; i < gridSize; i++)
        {
            for (int j = 0; j < gridSize; j++)
            {
                // First triangle of the cell
                mesh.Indices[index++] = i * (gridSize + 1) + j;           // Top-left
                mesh.Indices[index++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                mesh.Indices[index++] = i * (gridSize + 1) + j + 1;       // Top-right

                // Second triangle of the cell
                mesh.Indices[index++] = i * (gridSize + 1) + j + 1;       // Top-right
                mesh.Indices[index++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                mesh.Indices[index++] = (i + 1) * (gridSize + 1) + j + 1; // Bottom-right
            }
        }

        if (activeRegionModels.TryGetValue(sourceData.RegionIndex, out PerModelData existingData))
        {
            existingData.MeshRef.Dispose();
            activeRegionModels.Remove(sourceData.RegionIndex);
        }

        var meshRef = capi.Render.UploadMesh(mesh);

        // Return arrays to pool after GPU upload (data has been copied)
        ArrayPool<float>.Shared.Return(xyzArray);
        ArrayPool<int>.Shared.Return(indicesArray);

        activeRegionModels.Add(sourceData.RegionIndex, new PerModelData()
        {
            SourceData = sourceData,
            Position = new Vec3d(
                    sourceData.RegionX * sourceData.RegionSize,
                    0.0f,
                    sourceData.RegionZ * sourceData.RegionSize
                    ),
            MeshRef = meshRef,
        });

        if (!isRebuild)
        {
            // Mark neighbours as dirty for deferred rebuild (avoids cascading rebuilds in same frame)
            var westIdx = RegionNeighbourIndex(sourceData.RegionIndex, -1, 0, sourceData.RegionMapSize);
            var northIdx = RegionNeighbourIndex(sourceData.RegionIndex, 0, -1, sourceData.RegionMapSize);
            var northWestIdx = RegionNeighbourIndex(sourceData.RegionIndex, -1, -1, sourceData.RegionMapSize);

            if (activeRegionModels.TryGetValue(northIdx, out PerModelData northData) && GridSizesMatch(sourceData, northData.SourceData))
            {
                dirtyRegions.Add(northIdx);
            }
            if (activeRegionModels.TryGetValue(westIdx, out PerModelData westData) && GridSizesMatch(sourceData, westData.SourceData))
            {
                dirtyRegions.Add(westIdx);
            }
            if (activeRegionModels.TryGetValue(northWestIdx, out PerModelData northWestData) && GridSizesMatch(sourceData, northWestData.SourceData))
            {
                dirtyRegions.Add(northWestIdx);
            }
        }
    }

    public void UnloadRegion(long regionIdx)
    {
        if (activeRegionModels.TryGetValue(regionIdx, out PerModelData model))
        {
            model.MeshRef.Dispose();
            activeRegionModels.Remove(regionIdx);
        }
        dirtyRegions.Remove(regionIdx);
    }

    private void FlushDirtyRegions()
    {
        if (dirtyRegions.Count == 0) return;

        foreach (var regionIdx in dirtyRegions)
        {
            if (activeRegionModels.TryGetValue(regionIdx, out PerModelData data))
            {
                BuildRegion(data.SourceData, true);
            }
        }
        dirtyRegions.Clear();
    }

    public void ClearLoadedRegions()
    {
        foreach (var regionModel in activeRegionModels.Values)
        {
            regionModel.MeshRef.Dispose();
        }
        activeRegionModels.Clear();
        dirtyRegions.Clear();
    }

    public void Dispose()
    {
        ClearLoadedRegions();
        GC.SuppressFinalize(this);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        capi.World.FrameProfiler.Enter("farseer-render");

        var rapi = capi.Render;
        if (rapi.FrameWidth == 0) return;

        // Rebuild any regions marked dirty from previous frame (deferred neighbor stitching)
        FlushDirtyRegions();

        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        var viewDistance = (float)capi.World.Player.WorldData.DesiredViewDistance;

        var colorTintVec = new Vec4f(
            modSystem.Client.Config.ColorTintR,
            modSystem.Client.Config.ColorTintG,
            modSystem.Client.Config.ColorTintB,
            modSystem.Client.Config.ColorTintA
        );

        prog.Use();

        prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
        prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

        prog.Uniform("sunPosition", capi.World.Calendar.SunPositionNormalized);
        prog.Uniform("sunColor", capi.World.Calendar.SunColor);
        prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength));

        prog.Uniform("rgbaFogIn", capi.Ambient.BlendedFogColor);
        prog.Uniform("fogDensityIn", capi.Ambient.BlendedFogDensity);
        prog.Uniform("fogMinIn", capi.Ambient.BlendedFogMin);
        prog.Uniform("horizonFog", capi.Ambient.BlendedCloudDensity);

        prog.Uniform("skyTint", modSystem.Client.Config.SkyTint);
        prog.Uniform("colorTint", colorTintVec);
        prog.Uniform("lightLevelBias", modSystem.Client.Config.LightLevelBias);
        prog.Uniform("fadeBias", modSystem.Client.Config.FadeBias);
        prog.Uniform("globeEffect", modSystem.Client.Config.GlobeEffect);
        prog.Uniform("seaLevel", capi.World.SeaLevel);

        prog.Uniform("viewDistance", viewDistance);
        prog.Uniform("farViewDistance", (float)farViewDistance);

        foreach (var regionModel in activeRegionModels.Values)
        {
            modelMat.Identity()
                .Translate(regionModel.Position.X, regionModel.Position.Y, regionModel.Position.Z)
                .Translate(-camPos.X, -camPos.Y, -camPos.Z);

            prog.UniformMatrix("modelMatrix", modelMat.Values);

            rapi.RenderMesh(regionModel.MeshRef);

        }
        prog.Stop();

        capi.World.FrameProfiler.Leave();
    }
}
