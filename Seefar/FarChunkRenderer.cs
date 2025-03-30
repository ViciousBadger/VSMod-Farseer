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

    private MeshData mesh = new MeshData(4, 3, false, true, true, true);
    private MeshRef meshRef;
    private IShaderProgram prog;

    public FarChunkRenderer(ICoreClientAPI api, FarChunkMap map)
    {
        this.capi = api;
        this.map = map;

        var mesh = CubeMeshUtil.GetCubeOnlyScaleXyz(32f, 32f, new Vec3f());
        mesh.Rgba = new byte[6 * 4 * 4].Fill((byte)255);

        this.meshRef = this.capi.Render.UploadMesh(mesh);

        api.Event.ReloadShader += LoadShader;

        LoadShader();
    }

    public bool LoadShader()
    {
        prog = capi.Shader.NewShaderProgram();

        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        capi.Shader.RegisterFileShaderProgram("farchunks", prog);

        return prog.Compile();

    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var rapi = capi.Render;
        Vec3d camPos = capi.World.Player.Entity.CameraPos;


        foreach (var coord in this.map.LoadedChunks.Keys)
        {
            prog.Use();
            var chunk = map.LoadedChunks[coord];
            var minX = coord.X * 32;
            var maxX = minX + 32;
            var minZ = coord.Y * 32;
            var maxZ = minZ + 32;


            mvMat.Set(capi.Render.CameraMatrixOriginf).Translate(minX, 200, minZ);

            prog.UniformMatrix("modelViewMatrix", mvMat.Values);
            prog.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);

            rapi.RenderMesh(meshRef);

            // this.api.Logger.Notification(minX + " - " + maxX + ", " + minZ + " - " + maxZ);
            prog.Stop();
        }

    }

    public void Dispose()
    {
    }
}
