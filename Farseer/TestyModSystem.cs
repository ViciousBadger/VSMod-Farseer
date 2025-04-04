using Vintagestory.API.Client;
using Vintagestory.API.Server;
using Vintagestory.API.Common;

namespace Farseer;

public class TestyModSystem : ModSystem
{

    // Called on server and client
    // Useful for registering block/entity classes on both sides
    public override void Start(ICoreAPI api)
    {
        Mod.Logger.Notification("HELLLO");
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
    }

    public override void Dispose()
    {
    }

}
