using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using System;

namespace Seefar;

public class SeefarModSystem : ModSystem
{
    SeefarServer server;
    SeefarClient client;

    public static ILogger Logger;

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("Hello from template mod: " + api.Side);

        api.Network.RegisterChannel("seefar").RegisterMessageType<FarChunkMessage>();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Logger = Mod.Logger;
        this.server = new SeefarServer(this, api);
    }


    public override void StartClientSide(ICoreClientAPI api)
    {
        Logger = Mod.Logger;
        this.client = new SeefarClient(this, api);
    }

    public override void Dispose()
    {
        server?.Dispose();
        client?.Dispose();
    }

}
