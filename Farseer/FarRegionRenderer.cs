using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Farseer;

public class FarRegionRenderer : IRenderer
{
    public struct PerModelData
    {
        public Vec3d Position { get; set; }
        public MeshRef MeshRef { get; set; }
    }

    public double RenderOrder => 0.36;

    public int RenderRange => 9999;

    private ICoreClientAPI capi;
    private int farViewDistance;
    private Dictionary<long, PerModelData> activeRegionModels = new Dictionary<long, PerModelData>();

    private Matrixf modelMat = new Matrixf();
    private IShaderProgram prog;

    private EnumBlendMode blendMode = EnumBlendMode.Standard;

    public FarRegionRenderer(ICoreClientAPI capi, int farViewDistance)
    {
        this.capi = capi;
        this.farViewDistance = farViewDistance;

        capi.Event.ReloadShader += LoadShader;
        LoadShader();

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque);

        capi.Event.KeyDown += Blendtest;
    }

    private void Blendtest(KeyEvent e)
    {

        if (e.KeyCode == 84) //b
        {
            EnumBlendMode[] values = (EnumBlendMode[])Enum.GetValues(typeof(EnumBlendMode));
            int currentIndex = Array.IndexOf(values, blendMode);
            int nextIndex = (currentIndex + 1) % values.Length;
            blendMode = values[nextIndex];
            capi.Logger.Notification(blendMode.ToString());
        }
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

    public void BuildRegion(FarRegionData sourceData)
    {

        var mesh = new MeshData(false);

        var gridSize = sourceData.Heightmap.GridSize;
        float cellSize = sourceData.RegionSize / (float)gridSize;

        var vertexCount = (gridSize + 1) * (gridSize + 1);
        mesh.SetVerticesCount(vertexCount);
        mesh.xyz = new float[vertexCount * 3];

        var indicesCount = gridSize * gridSize * 6;
        mesh.SetIndicesCount(indicesCount);
        mesh.Indices = new int[indicesCount]; // 2 triangles per cell, 3 indices per triangle

        int xyz = 0;
        for (int i = 0; i <= gridSize; i++)
        {
            for (int j = 0; j <= gridSize; j++)
            {
                mesh.xyz[xyz++] = j * cellSize;
                // For vertices at the edges, use nearest available height data
                int hi = Math.Min(i, gridSize - 1);
                int hj = Math.Min(j, gridSize - 1);
                mesh.xyz[xyz++] = sourceData.Heightmap.Points[hi * gridSize + hj];
                mesh.xyz[xyz++] = i * cellSize;

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

        if (activeRegionModels.ContainsKey(sourceData.RegionIndex))
        {
            activeRegionModels.Remove(sourceData.RegionIndex);
        }

        activeRegionModels.Add(sourceData.RegionIndex, new PerModelData()
        {
            Position = new Vec3d(
                    sourceData.RegionX * sourceData.RegionSize,
                    0.0f,
                    sourceData.RegionZ * sourceData.RegionSize
                    ),
            MeshRef = capi.Render.UploadMesh(mesh),
        });

        capi.Logger.Notification("new region, pos x {0}, z {1}",
                    sourceData.RegionX * sourceData.RegionSize,
                    sourceData.RegionZ * sourceData.RegionSize
                );
    }

    public void UnloadRegion(long regionIdx)
    {
        if (activeRegionModels.TryGetValue(regionIdx, out PerModelData model))
        {
            model.MeshRef.Dispose();
            activeRegionModels.Remove(regionIdx);
        }
    }

    public void Dispose()
    {
        foreach (var regionModel in activeRegionModels.Values)
        {
            regionModel.MeshRef.Dispose();
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        var horizonColorDay = new Vec4f(0.525f, 0.620f, 0.776f, 1.0f);
        var horizonColorNight = new Vec4f(0.114f, 0.149f, 0.255f, 1.0f);

        foreach (var regionModel in activeRegionModels.Values)
        {
            prog.Use();

            modelMat.Identity()
                .Translate(regionModel.Position.X, regionModel.Position.Y, regionModel.Position.Z)
                .Translate(-camPos.X, -camPos.Y, -camPos.Z);

            prog.UniformMatrix("modelMatrix", modelMat.Values);
            prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
            prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

            prog.Uniform("horizonColorDay", horizonColorDay);
            prog.Uniform("horizonColorNight", horizonColorNight);
            prog.Uniform("fogColor", capi.Ambient.BlendedFogColor);
            prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength - capi.World.Calendar.MoonLightStrength * 0.95f));
            prog.Uniform("viewDistance", (float)capi.World.Player.WorldData.DesiredViewDistance);
            prog.Uniform("farViewDistance", (float)this.farViewDistance);

            rapi.GlToggleBlend(true, blendMode);
            rapi.RenderMesh(regionModel.MeshRef);

            prog.Stop();
        }
    }
}
