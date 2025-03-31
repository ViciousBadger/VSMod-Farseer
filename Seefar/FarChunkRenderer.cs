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

    private Matrixf mvMat = new Matrixf();

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

        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        capi.Shader.RegisterFileShaderProgram("farchunks", prog);

        var result = prog.Compile();

        return result;
    }

    private void RebuildModel()
    {
        if (meshRef != null)
        {
            meshRef.Dispose();
        }
        var mesh = new MeshData(false);
        mesh.SetXyz(new float[] { 0.0f, 0.0f, 0.0f, 32.0f, 0.0f, 0.0f, 0.0f, 0.0f, 32.0f, 32.0f, 0.0f, 32.0f });
        mesh.SetIndices(new int[] { 0, 1, 2, 2, 1, 3 });

        InitCustomDataBuffers(mesh);
        UpdateBufferContents(mesh);

        this.meshRef = this.capi.Render.UploadMesh(mesh);
    }

    void InitCustomDataBuffers(MeshData mesh)
    {
        mesh.CustomInts = new CustomMeshDataPartInt()
        {
            StaticDraw = false,
            Instanced = true,
            InterleaveSizes = new int[] { 2, 4 },
            InterleaveOffsets = new int[] { 0, 8 },
            InterleaveStride = 24,
            Conversion = DataConversion.Float,
            Values = new int[AmountOfFarChunks * 6],
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

            mesh.CustomInts.Values[pos++] = coord.X;
            mesh.CustomInts.Values[pos++] = coord.Y;
            mesh.CustomInts.Values[pos++] = chunk.Heightmap[0];
            mesh.CustomInts.Values[pos++] = chunk.Heightmap[1];
            mesh.CustomInts.Values[pos++] = chunk.Heightmap[2];
            mesh.CustomInts.Values[pos++] = chunk.Heightmap[3];
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;
        var plrPos = capi.World.Player.Entity.Pos;

        mvMat.Set(capi.Render.CameraMatrixOriginf).Translate(-plrPos.X, -plrPos.Y, -plrPos.Z);

        prog.Use();

        prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        prog.UniformMatrix("modelViewMatrix", mvMat.Values);
        //prog.UniformMatrix("modelMatrix", modelMatrix.Values);

        rapi.RenderMeshInstanced(meshRef, AmountOfFarChunks);

        prog.Stop();

        // foreach (var coord in this.map.LoadedChunks.Keys)
        // {
        //     prog.Use();
        //     var chunk = map.LoadedChunks[coord];
        //     var minX = coord.X * 32;
        //     var maxX = minX + 32;
        //     var minZ = coord.Y * 32;
        //     var maxZ = minZ + 32;
        //
        //     var plrPos = capi.World.Player.Entity.Pos;
        //
        //     var modelMatrix = modelMat.Identity().Translate(minX - plrPos.X, 200 - plrPos.Y, minZ - plrPos.Z);
        //
        //     prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        //     prog.UniformMatrix("viewMatrix", rapi.CameraMatrixOriginf);
        //     prog.UniformMatrix("modelMatrix", modelMatrix.Values);
        //
        //     //rapi.GlDisableCullFace();
        //
        //     rapi.RenderMesh(meshRef);
        //
        //     //rapi.GlEnableCullFace();
        //
        //     // this.api.Logger.Notification(minX + " - " + maxX + ", " + minZ + " - " + maxZ);
        //     prog.Stop();
        // }

    }

    public void Dispose()
    {
        capi.Render.DeleteMesh(meshRef);
        // capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
