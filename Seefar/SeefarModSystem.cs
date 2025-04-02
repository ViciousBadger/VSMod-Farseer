using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace Seefar;

public class SeefarModSystem : ModSystem
{
    SeefarServer server;
    SeefarClient client;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel("seefar")
            .RegisterMessageType<FarChunkMessage>()
            .RegisterMessageType<EnableSeefarRequest>()
            .RegisterMessageType<FarRegionRequest>()
            .RegisterMessageType<FarRegionData>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.server = new SeefarServer(this, api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.client = new SeefarClient(this, api);
    }

    public override void Dispose()
    {
        server?.Dispose();
        client?.Dispose();
    }

}
