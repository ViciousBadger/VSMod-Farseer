
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Seefar;

public class TestRenderer : IRenderer
{
    private ICoreClientAPI api;
    private MeshRef meshRef;

    private Matrixf modelMat = new Matrixf();

    public double RenderOrder => 0.36;

    public int RenderRange => 24;

    public TestRenderer(ICoreClientAPI api)
    {
        this.api = api;

        var mesh = CubeMeshUtil.GetCubeOnlyScaleXyz(32f, 32f, new Vec3f());
        mesh.Rgba = new byte[6 * 4 * 4].Fill((byte)255);
        //CubeMeshUtil.SetXyzFacesAndPacketNormals(mesh);

        this.meshRef = this.api.Render.UploadMesh(mesh);
        // byte[] rgba = CubeMeshUtil.GetShadedCubeRGBA(ColorUtil.WhiteArgb, CubeMeshUtil.DefaultBlockSideShadings, false);
        // mesh.SetRgba(rgba);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        IRenderAPI rpi = api.Render;
        IClientWorldAccessor worldAccess = api.World;
        Vec3d camPos = worldAccess.Player.Entity.CameraPos;

        var pos = new BlockPos(0, 320, 0);

        rpi.GlDisableCullFace();
        IStandardShaderProgram prog = rpi.PreparedStandardShader(pos.X, pos.Y, pos.Z);

        prog.ProjectionMatrix = rpi.CurrentProjectionMatrix;
        prog.ViewMatrix = rpi.CameraMatrixOriginf;
        prog.ModelMatrix = modelMat.Identity().Translate(pos.X - camPos.X,
                 pos.Y - camPos.Y, pos.Z - camPos.Z).Values;

        rpi.RenderMesh(this.meshRef);

        prog.ModelMatrix = rpi.CurrentModelviewMatrix;
        prog.Stop();

        var playerPos = this.api.World.Player.Entity.Pos.AsBlockPos;
        this.api.Render.RenderLine(playerPos, 1f, 1f, 1f, -1f, 1f, 1f, 255);
    }

    public void Dispose()
    {
        this.meshRef.Dispose();
    }
}
