using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Farseer;

public class FarRegionRenderer : IRenderer
{
    public struct PerModelData
    {
        public FarRegionData SourceData { get; set; }
        public Vec3d Position { get; set; }
        public MeshRef MeshRef { get; set; }
        public int GridSize { get; set; }
    }

    public double RenderOrder => 0.36;

    public int RenderRange => 9999;

    private FarseerModSystem modSystem;
    private ICoreClientAPI capi;
    private Dictionary<long, PerModelData> activeRegionModels = new Dictionary<long, PerModelData>();

    private Matrixf modelMat = new Matrixf();
    private IShaderProgram prog;

    private int farViewDistance = 3072;
    
    // Reusable mesh data for updates
    private MeshData reusableMesh = new MeshData(false);
    
    // Cache for neighbor lookups
    private PerModelData[] neighborCache = new PerModelData[4];
    private bool[] neighborFound = new bool[4];
    
    // LOD update tracking
    private double lodUpdateAccumulator = 0;
    private const double LOD_UPDATE_INTERVAL = 1.0; // Check LOD every 1 second

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

        var clientMain = ((ClientMain)capi.World);
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

    private long RegionNeighbourIndex(long idx, int offsetX, int offsetZ, int regionMapSize)
    {
        int rX = (int)(idx % regionMapSize);
        int rZ = (int)(idx / regionMapSize);

        return (long)(rZ + offsetZ) * (long)regionMapSize + (long)(rX + offsetX);
    }

    public void BuildRegion(FarRegionData sourceData, bool isRebuild = false)
    {
        bool GridSizesMatch(FarRegionData regionA, FarRegionData regionB)
        {
            return regionA.Heightmap.GridSize == regionB.Heightmap.GridSize;
        }

        // Calculate distance-based LOD
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        Vec3d regionCenter = new Vec3d(
            sourceData.RegionX * sourceData.RegionSize + sourceData.RegionSize / 2.0,
            0,
            sourceData.RegionZ * sourceData.RegionSize + sourceData.RegionSize / 2.0
        );
        double distSq = camPos.SquareDistanceTo(regionCenter);
        
        // Determine LOD level with hysteresis to prevent thrashing
        int baseGridSize = sourceData.Heightmap.GridSize;
        int lodGridSize = baseGridSize;
        
        // Get current LOD if region exists
        int currentLOD = 0; // 0 = full, 1 = half, 2 = quarter
        if (activeRegionModels.TryGetValue(sourceData.RegionIndex, out PerModelData existingData))
        {
            if (existingData.GridSize == baseGridSize) currentLOD = 0;
            else if (existingData.GridSize == Math.Max(32, baseGridSize / 2)) currentLOD = 1;
            else if (existingData.GridSize == Math.Max(32, baseGridSize / 4)) currentLOD = 2;
        }
        
        // LOD thresholds with hysteresis (10% margin to prevent flickering)
        double lod1Threshold = 2048 * 2048;
        double lod2Threshold = 4096 * 4096;
        double hysteresis = 0.1;
        
        if (distSq > lod2Threshold * (1.0 + (currentLOD == 2 ? -hysteresis : hysteresis)))
        {
            lodGridSize = Math.Max(32, baseGridSize / 4); // Quarter resolution
        }
        else if (distSq > lod1Threshold * (1.0 + (currentLOD == 1 ? -hysteresis : hysteresis)))
        {
            lodGridSize = Math.Max(32, baseGridSize / 2); // Half resolution
        }
        // else: full resolution

        // Find neighbour id's for stitching
        var eastIdx = RegionNeighbourIndex(sourceData.RegionIndex, 1, 0, sourceData.RegionMapSize);
        var southIdx = RegionNeighbourIndex(sourceData.RegionIndex, 0, 1, sourceData.RegionMapSize);
        var southEastIdx = RegionNeighbourIndex(sourceData.RegionIndex, 1, 1, sourceData.RegionMapSize);

        // Cache neighbor lookups
        neighborFound[0] = activeRegionModels.TryGetValue(eastIdx, out neighborCache[0]);
        neighborFound[1] = activeRegionModels.TryGetValue(southIdx, out neighborCache[1]);
        neighborFound[2] = activeRegionModels.TryGetValue(southEastIdx, out neighborCache[2]);

        var gridSize = lodGridSize; // Use LOD grid size instead of source
        float cellSize = sourceData.RegionSize / (float)gridSize;
        float sourceGridSize = sourceData.Heightmap.GridSize;
        float sourceCellSize = sourceData.RegionSize / sourceGridSize;

        var vertexCount = (gridSize + 1) * (gridSize + 1);
        var indicesCount = gridSize * gridSize * 6;

        // Check if we can update existing mesh
        bool canUpdate = activeRegionModels.TryGetValue(sourceData.RegionIndex, out existingData) &&
                        existingData.GridSize == gridSize;

        if (canUpdate)
        {
            // Only update vertices, reuse indices
            reusableMesh.SetVerticesCount(vertexCount);
            if (reusableMesh.xyz == null || reusableMesh.xyz.Length != vertexCount * 3)
            {
                reusableMesh.xyz = new float[vertexCount * 3];
            }
            reusableMesh.Indices = null; // Don't update indices
        }
        else
        {
            // Full mesh creation
            reusableMesh.SetVerticesCount(vertexCount);
            reusableMesh.xyz = new float[vertexCount * 3];
            reusableMesh.SetIndicesCount(indicesCount);
            reusableMesh.Indices = new int[indicesCount];
        }

        int xyz = 0;
        int vertexIndex = 0;
        for (int vZ = 0; vZ <= gridSize; vZ++)
        {
            for (int vX = 0; vX <= gridSize; vX++)
            {
                reusableMesh.xyz[xyz++] = vX * cellSize;

                int sample = 0;

                if (vX == gridSize && vZ == gridSize && neighborFound[2] && GridSizesMatch(sourceData, neighborCache[2].SourceData))
                {
                    // For corner, select north-western-most point south-east neighbour 
                    sample = neighborCache[2].SourceData.Heightmap.Points[0];
                }
                else if (vX == gridSize && vZ < gridSize && neighborFound[0] && GridSizesMatch(sourceData, neighborCache[0].SourceData))
                {
                    // For x end, select west-most point of east neighbour
                    sample = neighborCache[0].SourceData.Heightmap.Points[vZ * gridSize];
                }
                else if (vZ == gridSize && vX < gridSize && neighborFound[1] && GridSizesMatch(sourceData, neighborCache[1].SourceData))
                {
                    // For z end, select north-most point of south neighbour
                    sample = neighborCache[1].SourceData.Heightmap.Points[vX];
                }
                else
                {
                    // Sample from source heightmap with LOD scaling
                    float sourceX = (vX * cellSize) / sourceCellSize;
                    float sourceZ = (vZ * cellSize) / sourceCellSize;
                    int sX = Math.Min((int)sourceX, (int)sourceGridSize - 1);
                    int sZ = Math.Min((int)sourceZ, (int)sourceGridSize - 1);
                    int sourceIdx = sZ * (int)sourceGridSize + sX;
                    sample = sourceData.Heightmap.Points[sourceIdx];
                }

                reusableMesh.xyz[xyz++] = sample;
                reusableMesh.xyz[xyz++] = vZ * cellSize;

                vertexIndex++;
            }
        }

        if (!canUpdate)
        {
            // Build indices only for new meshes
            int index = 0;
            for (int i = 0; i < gridSize; i++)
            {
                for (int j = 0; j < gridSize; j++)
                {
                    // First triangle of the cell
                    reusableMesh.Indices[index++] = i * (gridSize + 1) + j;           // Top-left
                    reusableMesh.Indices[index++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                    reusableMesh.Indices[index++] = i * (gridSize + 1) + j + 1;       // Top-right

                    // Second triangle of the cell
                    reusableMesh.Indices[index++] = i * (gridSize + 1) + j + 1;       // Top-right
                    reusableMesh.Indices[index++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                    reusableMesh.Indices[index++] = (i + 1) * (gridSize + 1) + j + 1; // Bottom-right
                }
            }
        }

        if (canUpdate)
        {
            // Update existing mesh
            try
            {
                capi.Render.UpdateMesh(existingData.MeshRef, reusableMesh);
                existingData.SourceData = sourceData;
                activeRegionModels[sourceData.RegionIndex] = existingData;
            }
            catch (Exception e)
            {
                modSystem.Mod.Logger.Error($"Failed to update mesh for region {sourceData.RegionIndex}", e);
                // Try full rebuild as fallback
                try
                {
                    existingData.MeshRef.Dispose();
                    activeRegionModels[sourceData.RegionIndex] = new PerModelData()
                    {
                        SourceData = sourceData,
                        Position = new Vec3d(sourceData.RegionX * sourceData.RegionSize, 0.0f, sourceData.RegionZ * sourceData.RegionSize),
                        MeshRef = capi.Render.UploadMesh(reusableMesh),
                        GridSize = gridSize,
                    };
                }
                catch (Exception e2)
                {
                    modSystem.Mod.Logger.Error($"Failed to rebuild mesh for region {sourceData.RegionIndex}", e2);
                    return; // Give up on this region
                }
            }
        }
        else
        {
            // Upload new mesh
            try
            {
                if (activeRegionModels.TryGetValue(sourceData.RegionIndex, out PerModelData oldData))
                {
                    oldData.MeshRef.Dispose();
                }

                activeRegionModels[sourceData.RegionIndex] = new PerModelData()
                {
                    SourceData = sourceData,
                    Position = new Vec3d(
                            sourceData.RegionX * sourceData.RegionSize,
                            0.0f,
                            sourceData.RegionZ * sourceData.RegionSize
                            ),
                    MeshRef = capi.Render.UploadMesh(reusableMesh),
                    GridSize = gridSize,
                };
            }
            catch (Exception e)
            {
                modSystem.Mod.Logger.Error($"Failed to upload mesh for region {sourceData.RegionIndex}", e);
                return; // Skip this region
            }
        }

        if (!isRebuild)
        {
            // Re-build neighbours that are affected by this new data.
            var westIdx = RegionNeighbourIndex(sourceData.RegionIndex, -1, 0, sourceData.RegionMapSize);
            var northIdx = RegionNeighbourIndex(sourceData.RegionIndex, 0, -1, sourceData.RegionMapSize);
            var northWestIdx = RegionNeighbourIndex(sourceData.RegionIndex, -1, -1, sourceData.RegionMapSize);


            if (activeRegionModels.TryGetValue(northIdx, out PerModelData northData) && GridSizesMatch(sourceData, northData.SourceData))
            {
                BuildRegion(northData.SourceData, true);
            }
            if (activeRegionModels.TryGetValue(westIdx, out PerModelData westData) && GridSizesMatch(sourceData, westData.SourceData))
            {
                BuildRegion(westData.SourceData, true);
            }
            if (activeRegionModels.TryGetValue(northWestIdx, out PerModelData northWestData) && GridSizesMatch(sourceData, northWestData.SourceData))
            {
                BuildRegion(northWestData.SourceData, true);
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
    }

    public void ClearLoadedRegions()
    {
        foreach (var regionModel in activeRegionModels.Values)
        {
            regionModel.MeshRef.Dispose();
        }
        activeRegionModels.Clear();
    }

    public void Dispose()
    {
        ClearLoadedRegions();
    }

    private void UpdateLODLevels(Vec3d camPos)
    {
        // Check each region and rebuild if LOD level should change
        foreach (var pair in activeRegionModels)
        {
            var regionData = pair.Value.SourceData;
            Vec3d regionCenter = new Vec3d(
                regionData.RegionX * regionData.RegionSize + regionData.RegionSize / 2.0,
                0,
                regionData.RegionZ * regionData.RegionSize + regionData.RegionSize / 2.0
            );
            double distSq = camPos.SquareDistanceTo(regionCenter);
            
            int baseGridSize = regionData.Heightmap.GridSize;
            int currentGridSize = pair.Value.GridSize;
            
            // Determine what LOD level should be
            int targetGridSize = baseGridSize;
            if (distSq > 4096 * 4096 * 1.1) // 10% hysteresis
            {
                targetGridSize = Math.Max(32, baseGridSize / 4);
            }
            else if (distSq > 2048 * 2048 * 1.1)
            {
                targetGridSize = Math.Max(32, baseGridSize / 2);
            }
            
            // Rebuild if LOD changed
            if (targetGridSize != currentGridSize)
            {
                BuildRegion(regionData, true);
            }
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var rapi = capi.Render;
        if (rapi.FrameWidth == 0) return;
        if (activeRegionModels.Count == 0) return;

        var profiler = capi.World.FrameProfiler;
        if (profiler?.Enabled == true)
        {
            profiler.Enter("farseer-render");
        }

        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        var viewDistance = (float)capi.World.Player.WorldData.DesiredViewDistance;

        // Periodic LOD update check
        lodUpdateAccumulator += deltaTime;
        if (lodUpdateAccumulator >= LOD_UPDATE_INTERVAL)
        {
            lodUpdateAccumulator = 0;
            UpdateLODLevels(camPos);
        }

        // Frustum culling bounds
        double frustumCullDist = farViewDistance * 1.2; // Add margin
        double frustumCullDistSq = frustumCullDist * frustumCullDist;

        var colorTintVec = new Vec4f(
            modSystem.Client.Config.ColorTintR,
            modSystem.Client.Config.ColorTintG,
            modSystem.Client.Config.ColorTintB,
            modSystem.Client.Config.ColorTintA
        );

        if (profiler?.Enabled == true) profiler.Mark("farseer-setup");

        // Batch all regions under single shader bind
        prog.Use();

        // Set uniforms once for all regions
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
        prog.Uniform("minDrawDistance", (float)modSystem.Client.Config.MinDrawDistance);

        prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
        prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

        if (profiler?.Enabled == true) profiler.Mark("farseer-uniforms");

        int regionsRendered = 0;
        foreach (var regionModel in activeRegionModels.Values)
        {
            // Simple frustum culling - check distance to camera
            double dx = regionModel.Position.X - camPos.X;
            double dz = regionModel.Position.Z - camPos.Z;
            double distSq = dx * dx + dz * dz;

            if (distSq > frustumCullDistSq) continue;

            modelMat.Identity()
                .Translate(regionModel.Position.X, regionModel.Position.Y, regionModel.Position.Z)
                .Translate(-camPos.X, -camPos.Y, -camPos.Z);

            prog.UniformMatrix("modelMatrix", modelMat.Values);

            rapi.RenderMesh(regionModel.MeshRef);
            regionsRendered++;
        }

        prog.Stop();

        if (profiler?.Enabled == true)
        {
            profiler.Mark("farseer-render-" + regionsRendered + "-regions");
            profiler.Leave();
        }
    }
}
