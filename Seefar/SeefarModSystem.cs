using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;

namespace Seefar;

public class SeefarModSystem : ModSystem
{

    TestRenderer renderer;
    ICoreClientAPI clientApi;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("Hello from template mod: " + api.Side);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Mod.Logger.Notification("Hello from template mod server side: " + Lang.Get("seefar:hello"));
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.renderer = new TestRenderer(api);
        this.clientApi = api;
        api.Event.RegisterRenderer(this.renderer, EnumRenderStage.Opaque);
    }

    public override void Dispose()
    {
        if (this.renderer != null && this.clientApi != null)
        {
            this.clientApi.Event.UnregisterRenderer(this.renderer, EnumRenderStage.Opaque);
        }
    }

}
