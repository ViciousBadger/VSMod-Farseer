using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace Farseer;

public class FarseerModSystem : ModSystem
{
    FarseerServer server;
    FarseerClient client;

    public const string MOD_CHANNEL_NAME = "farseer";

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(MOD_CHANNEL_NAME)
            .RegisterMessageType<FarChunkMessage>()
            .RegisterMessageType<FarEnableRequest>()
            .RegisterMessageType<FarRegionRequest>()
            .RegisterMessageType<FarRegionData>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        this.server = new FarseerServer(this, api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        this.client = new FarseerClient(this, api);
    }

    public override void Dispose()
    {
        server?.Dispose();
        client?.Dispose();
    }

}
