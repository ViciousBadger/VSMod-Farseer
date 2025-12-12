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
        public double LastRebuildTime { get; set; }
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
    private readonly Dictionary<(int grid, int seamMask, bool skirts), int[]> indexCache = new();
    
    // Cache for neighbor lookups
    private PerModelData[] neighborCache = new PerModelData[4];
    private bool[] neighborFound = new bool[4];
    
    // LOD update tracking
    private double lodUpdateAccumulator = 0;
    private const double LOD_UPDATE_INTERVAL = 1.0; // Check LOD every 1 second
    private const double REBUILD_THROTTLE_MS = 350.0;
    private const float SKIRT_DEPTH = 3.0f;

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

    private bool IsSkirtEnabled() => SKIRT_DEPTH > 0.01f;

    public void BuildRegion(FarRegionData sourceData, bool isRebuild = false)
    {
        var profiler = capi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter(isRebuild ? "farseer-buildregion-rebuild" : "farseer-buildregion");

        double nowMs = capi.World.ElapsedMilliseconds;
        PerModelData existingData;
        bool hasExisting = activeRegionModels.TryGetValue(sourceData.RegionIndex, out existingData);
        int previousGridSize = hasExisting ? existingData.GridSize : -1;

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
        if (hasExisting)
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

        // Base grid vertex/index counts
        var baseVertexCount = (gridSize + 1) * (gridSize + 1);
        var baseIndicesCount = gridSize * gridSize * 6;

        // Seam vertices/indices when LOD differs
        int seamVertices = 0;
        int seamIndices = 0;
        int seamMask = 0; // bit 0: east, bit1: south, bit2: corner

        // Count seam data for each edge with differing grid sizes
        if (neighborFound[0] && !GridSizesMatch(sourceData, neighborCache[0].SourceData))
        {
            // East edge seam: one strip of gridSize quads
            seamVertices += (gridSize + 1) * 2;
            seamIndices += gridSize * 6;
            if (IsSkirtEnabled())
            {
                seamVertices += (gridSize + 1) * 2;
                seamIndices += gridSize * 6;
            }
            seamMask |= 1;
        }
        if (neighborFound[1] && !GridSizesMatch(sourceData, neighborCache[1].SourceData))
        {
            // South edge seam
            seamVertices += (gridSize + 1) * 2;
            seamIndices += gridSize * 6;
            if (IsSkirtEnabled())
            {
                seamVertices += (gridSize + 1) * 2;
                seamIndices += gridSize * 6;
            }
            seamMask |= 2;
        }
        if (neighborFound[2] && !GridSizesMatch(sourceData, neighborCache[2].SourceData))
        {
            // Corner seam (single quad strip)
            seamVertices += 4;
            seamIndices += 6;
            seamMask |= 4;
        }

        var vertexCount = baseVertexCount + seamVertices;
        var indicesCount = baseIndicesCount + seamIndices;

        if (isRebuild && hasExisting && existingData.GridSize == gridSize && (nowMs - existingData.LastRebuildTime) < REBUILD_THROTTLE_MS)
        {
            if (profile) profiler.Leave();
            return;
        }

        // Check if we can update existing mesh
        bool canUpdate = hasExisting && existingData.GridSize == gridSize;

        if (canUpdate)
        {
            // Only update vertices, reuse capacity
            reusableMesh.SetVerticesCount(vertexCount);
            if (reusableMesh.xyz == null || reusableMesh.xyz.Length != vertexCount * 3)
            {
                reusableMesh.xyz = new float[vertexCount * 3];
            }
            reusableMesh.SetIndicesCount(indicesCount);
            if (reusableMesh.Indices == null || reusableMesh.Indices.Length != indicesCount)
            {
                reusableMesh.Indices = new int[indicesCount];
            }
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

        // Build seam vertices (east, south, skirts, corner)
        // East seam: x = gridSize, interpolate neighbor coarse edge to fine
        if (neighborFound[0] && !GridSizesMatch(sourceData, neighborCache[0].SourceData))
        {
            float neighborCell = neighborCache[0].GridSize > 0 ? sourceData.RegionSize / neighborCache[0].GridSize : cellSize;
            for (int k = 0; k <= gridSize; k++)
            {
                // top strip (fine edge)
                reusableMesh.xyz[xyz++] = gridSize * cellSize;
                reusableMesh.xyz[xyz++] = reusableMesh.xyz[(k * (gridSize + 1) + gridSize) * 3 + 1]; // reuse height
                reusableMesh.xyz[xyz++] = k * cellSize;

                // bottom strip (coarse neighbor edge sample)
                int neighborIdx = Math.Min(neighborCache[0].GridSize - 1, (int)(k * (neighborCache[0].GridSize - 1f) / gridSize));
                float sample = neighborCache[0].SourceData.Heightmap.Points[neighborIdx * neighborCache[0].GridSize];
                reusableMesh.xyz[xyz++] = gridSize * cellSize;
                reusableMesh.xyz[xyz++] = sample;
                reusableMesh.xyz[xyz++] = k * cellSize;
            }

            if (IsSkirtEnabled())
            {
                for (int k = 0; k <= gridSize; k++)
                {
                    // top of skirt = neighbor edge height
                    int neighborIdx = Math.Min(neighborCache[0].GridSize - 1, (int)(k * (neighborCache[0].GridSize - 1f) / gridSize));
                    float sample = neighborCache[0].SourceData.Heightmap.Points[neighborIdx * neighborCache[0].GridSize];
                    reusableMesh.xyz[xyz++] = gridSize * cellSize;
                    reusableMesh.xyz[xyz++] = sample;
                    reusableMesh.xyz[xyz++] = k * cellSize;

                    // bottom of skirt
                    reusableMesh.xyz[xyz++] = gridSize * cellSize;
                    reusableMesh.xyz[xyz++] = sample - SKIRT_DEPTH;
                    reusableMesh.xyz[xyz++] = k * cellSize;
                }
            }
        }

        // South seam: z = gridSize
        if (neighborFound[1] && !GridSizesMatch(sourceData, neighborCache[1].SourceData))
        {
            for (int k = 0; k <= gridSize; k++)
            {
                // left strip (fine edge)
                reusableMesh.xyz[xyz++] = k * cellSize;
                reusableMesh.xyz[xyz++] = reusableMesh.xyz[(gridSize * (gridSize + 1) + k) * 3 + 1];
                reusableMesh.xyz[xyz++] = gridSize * cellSize;

                // right strip (coarse neighbor edge sample)
                int neighborIdx = Math.Min(neighborCache[1].GridSize - 1, (int)(k * (neighborCache[1].GridSize - 1f) / gridSize));
                float sample = neighborCache[1].SourceData.Heightmap.Points[neighborIdx];
                reusableMesh.xyz[xyz++] = k * cellSize;
                reusableMesh.xyz[xyz++] = sample;
                reusableMesh.xyz[xyz++] = gridSize * cellSize;
            }

            if (IsSkirtEnabled())
            {
                for (int k = 0; k <= gridSize; k++)
                {
                    int neighborIdx = Math.Min(neighborCache[1].GridSize - 1, (int)(k * (neighborCache[1].GridSize - 1f) / gridSize));
                    float sample = neighborCache[1].SourceData.Heightmap.Points[neighborIdx];
                    reusableMesh.xyz[xyz++] = k * cellSize;
                    reusableMesh.xyz[xyz++] = sample;
                    reusableMesh.xyz[xyz++] = gridSize * cellSize;

                    reusableMesh.xyz[xyz++] = k * cellSize;
                    reusableMesh.xyz[xyz++] = sample - SKIRT_DEPTH;
                    reusableMesh.xyz[xyz++] = gridSize * cellSize;
                }
            }
        }

        // Corner seam: use NE/SW interpolation (simplified)
        if (neighborFound[2] && !GridSizesMatch(sourceData, neighborCache[2].SourceData))
        {
            // Use SE corner heights from fine and neighbor SE
            float fineHeight = reusableMesh.xyz[(gridSize * (gridSize + 1) + gridSize) * 3 + 1];
            float neighborHeight = neighborCache[2].SourceData.Heightmap.Points[0];

            reusableMesh.xyz[xyz++] = gridSize * cellSize;
            reusableMesh.xyz[xyz++] = fineHeight;
            reusableMesh.xyz[xyz++] = gridSize * cellSize;

            reusableMesh.xyz[xyz++] = gridSize * cellSize;
            reusableMesh.xyz[xyz++] = neighborHeight;
            reusableMesh.xyz[xyz++] = gridSize * cellSize;

            reusableMesh.xyz[xyz++] = gridSize * cellSize;
            reusableMesh.xyz[xyz++] = fineHeight;
            reusableMesh.xyz[xyz++] = gridSize * cellSize;

            reusableMesh.xyz[xyz++] = gridSize * cellSize;
            reusableMesh.xyz[xyz++] = neighborHeight;
            reusableMesh.xyz[xyz++] = gridSize * cellSize;
        }

        // Try to reuse cached indices
        var cacheKey = (gridSize, seamMask, IsSkirtEnabled());
        if (!indexCache.TryGetValue(cacheKey, out int[] cachedIndices) || cachedIndices.Length != indicesCount)
        {
            var indices = new int[indicesCount];
            int idx = 0;
            for (int i = 0; i < gridSize; i++)
            {
                for (int j = 0; j < gridSize; j++)
                {
                    // First triangle of the cell
                    indices[idx++] = i * (gridSize + 1) + j;           // Top-left
                    indices[idx++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                    indices[idx++] = i * (gridSize + 1) + j + 1;       // Top-right

                    // Second triangle of the cell
                    indices[idx++] = i * (gridSize + 1) + j + 1;       // Top-right
                    indices[idx++] = (i + 1) * (gridSize + 1) + j;     // Bottom-left
                    indices[idx++] = (i + 1) * (gridSize + 1) + j + 1; // Bottom-right
                }
            }

            int seamStart = baseIndicesCount;
            int seamVertexCursor = baseVertexCount;

            // East seam
            if ((seamMask & 1) != 0)
            {
                for (int k = 0; k < gridSize; k++)
                {
                    int v0 = seamVertexCursor + k;
                    int v1 = seamVertexCursor + k + 1;
                    int v2 = seamVertexCursor + (gridSize + 1) + k;
                    int v3 = seamVertexCursor + (gridSize + 1) + k + 1;

                    indices[seamStart++] = v0;
                    indices[seamStart++] = v2;
                    indices[seamStart++] = v1;

                    indices[seamStart++] = v1;
                    indices[seamStart++] = v2;
                    indices[seamStart++] = v3;
                }
                seamVertexCursor += (gridSize + 1) * 2;

                if (cacheKey.Item3) // skirts
                {
                    for (int k = 0; k < gridSize; k++)
                    {
                        int v0 = seamVertexCursor + k;
                        int v1 = seamVertexCursor + k + 1;
                        int v2 = seamVertexCursor + (gridSize + 1) + k;
                        int v3 = seamVertexCursor + (gridSize + 1) + k + 1;

                        indices[seamStart++] = v0;
                        indices[seamStart++] = v2;
                        indices[seamStart++] = v1;

                        indices[seamStart++] = v1;
                        indices[seamStart++] = v2;
                        indices[seamStart++] = v3;
                    }
                    seamVertexCursor += (gridSize + 1) * 2;
                }
            }

            // South seam
            if ((seamMask & 2) != 0)
            {
                for (int k = 0; k < gridSize; k++)
                {
                    int v0 = seamVertexCursor + k;
                    int v1 = seamVertexCursor + k + 1;
                    int v2 = seamVertexCursor + (gridSize + 1) + k;
                    int v3 = seamVertexCursor + (gridSize + 1) + k + 1;

                    indices[seamStart++] = v0;
                    indices[seamStart++] = v2;
                    indices[seamStart++] = v1;

                    indices[seamStart++] = v1;
                    indices[seamStart++] = v2;
                    indices[seamStart++] = v3;
                }
                seamVertexCursor += (gridSize + 1) * 2;

                if (cacheKey.Item3)
                {
                    for (int k = 0; k < gridSize; k++)
                    {
                        int v0 = seamVertexCursor + k;
                        int v1 = seamVertexCursor + k + 1;
                        int v2 = seamVertexCursor + (gridSize + 1) + k;
                        int v3 = seamVertexCursor + (gridSize + 1) + k + 1;

                        indices[seamStart++] = v0;
                        indices[seamStart++] = v2;
                        indices[seamStart++] = v1;

                        indices[seamStart++] = v1;
                        indices[seamStart++] = v2;
                        indices[seamStart++] = v3;
                    }
                    seamVertexCursor += (gridSize + 1) * 2;
                }
            }

            // Corner seam
            if ((seamMask & 4) != 0)
            {
                int v0 = seamVertexCursor + 0;
                int v1 = seamVertexCursor + 1;
                int v2 = seamVertexCursor + 2;
                int v3 = seamVertexCursor + 3;

                indices[seamStart++] = v0;
                indices[seamStart++] = v2;
                indices[seamStart++] = v1;

                indices[seamStart++] = v1;
                indices[seamStart++] = v2;
                indices[seamStart++] = v3;
            }

            cachedIndices = indices;
            indexCache[cacheKey] = cachedIndices;
        }

        // Apply cached indices
        reusableMesh.SetIndicesCount(cachedIndices.Length);
        reusableMesh.Indices = cachedIndices;

        if (canUpdate)
        {
            // Update existing mesh
            try
            {
                capi.Render.UpdateMesh(existingData.MeshRef, reusableMesh);
                existingData.SourceData = sourceData;
                existingData.LastRebuildTime = nowMs;
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
                        LastRebuildTime = nowMs,
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
                    LastRebuildTime = nowMs,
                };
            }
            catch (Exception e)
            {
                modSystem.Mod.Logger.Error($"Failed to upload mesh for region {sourceData.RegionIndex}", e);
                return; // Skip this region
            }
        }

        bool gridSizeChanged = previousGridSize < 0 || previousGridSize != gridSize;

        if (!isRebuild && gridSizeChanged)
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

        if (profile) profiler.Leave();
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
