using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Seefar;

public class FarChunkRenderer : IRenderer
{
    public double RenderOrder => 0.36;

    public int RenderRange => 9999;

    private ICoreClientAPI capi;
    private FarChunkMap map;

    private Matrixf modelMat = new Matrixf();

    private IShaderProgram prog;

    private MeshRef meshRef;
    // private MeshData updateMesh = new MeshData()
    // {
    //     CustomInts = new CustomMeshDataPartInt()
    // };

    private int AmountOfFarChunks => map.LoadedChunks.Count;

    public FarChunkRenderer(ICoreClientAPI api, FarChunkMap map)
    {
        this.capi = api;
        this.map = map;


        api.Event.ReloadShader += LoadShader;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque);

        LoadShader();
        RebuildModel();

        map.NewChunkLoaded += (_, _) =>
        {
            RebuildModel();
        };
    }

    public bool LoadShader()
    {
        prog = capi.Shader.NewShaderProgram();

        prog.AssetDomain = "seefar";
        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        capi.Shader.RegisterFileShaderProgram("farchunks", prog);

        var result = prog.Compile();

        return result;
    }

    private void RebuildModel()
    {
        var mesh = new MeshData(false);
        mesh.SetXyz(new float[] {
                0.0f, 0.0f, 0.0f,
                32.0f, 0.0f, 0.0f,
                0.0f, 0.0f, 32.0f,
                32.0f, 0.0f, 32.0f
        });
        mesh.SetVerticesCount(4);
        mesh.SetIndices(new int[] { 0, 2, 1, 2, 3, 1 });
        mesh.SetIndicesCount(6);

        InitCustomDataBuffers(mesh);
        UpdateBufferContents(mesh);

        this.meshRef?.Dispose();
        this.meshRef = this.capi.Render.UploadMesh(mesh);
    }

    void InitCustomDataBuffers(MeshData mesh)
    {
        mesh.CustomFloats = new CustomMeshDataPartFloat()
        {
            StaticDraw = true,
            Instanced = true,
            InterleaveSizes = new int[] { 2, 4 },
            InterleaveOffsets = new int[] { 0, 8 },
            InterleaveStride = 24,
            // Conversion = DataConversion.Float,
            Values = new float[AmountOfFarChunks * 6],
            Count = AmountOfFarChunks * 6
        };
    }

    void UpdateBufferContents(MeshData mesh)
    {
        int pos = 0;

        foreach (var pair in map.LoadedChunks)
        {
            var coord = pair.Key;
            var chunk = pair.Value;

            mesh.CustomFloats.Values[pos++] = coord.X;
            mesh.CustomFloats.Values[pos++] = coord.Y;
            mesh.CustomFloats.Values[pos++] = chunk.Heightmap[0];
            mesh.CustomFloats.Values[pos++] = chunk.Heightmap[1];
            mesh.CustomFloats.Values[pos++] = chunk.Heightmap[2];
            mesh.CustomFloats.Values[pos++] = chunk.Heightmap[3];
        }

        // capi.Logger.Notification("[{0}]", string.Join(", ", mesh.CustomInts.Values));
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;

        prog.Use();

        modelMat.Identity().Translate(-camPos.X, -camPos.Y, -camPos.Z);
        prog.UniformMatrix("modelMatrix", modelMat.Values);
        prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
        prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

        // From sky.png
        var horizonColorDay = new Vec4f(0.525f, 0.620f, 0.776f, 1.0f);
        var horizonColorNight = new Vec4f(0.114f, 0.149f, 0.255f, 1.0f);

        prog.Uniform("horizonColorDay", horizonColorDay);
        prog.Uniform("horizonColorNight", horizonColorNight);
        prog.Uniform("fogColor", capi.Ambient.BlendedFogColor);

        prog.Uniform("dayLight", Math.Max(0, capi.World.Calendar.DayLightStrength - capi.World.Calendar.MoonLightStrength * 0.95f));
        prog.Uniform("viewDistance", (float)capi.World.Player.WorldData.DesiredViewDistance);

        rapi.GlToggleBlend(true, EnumBlendMode.Standard);

        rapi.RenderMeshInstanced(meshRef, AmountOfFarChunks);

        prog.Stop();
    }

    public void Dispose()
    {
        capi.Render.DeleteMesh(meshRef);
        // capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
