using Vintagestory.API.Server;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common;

namespace Farseer;

/// Somewhat maybe reliable mechanism for sending batches of far region data at regular intervals, to avoid flooding the clients with data to process, causing lag.
public class BatchedRegionDataBuffer
{
    record Packet
    {
        public FarRegionData RegionData { get; init; }
        public IServerPlayer[] Targets { get; set; }
    }

    private int batchSize;
    private Queue<Packet> sendQueue = new();
    private ModSystem modSystem;
    private ICoreServerAPI sapi;

    public BatchedRegionDataBuffer(ModSystem modSystem, ICoreServerAPI sapi, int batchSize)
    {
        this.modSystem = modSystem;
        this.sapi = sapi;
        this.batchSize = batchSize;
    }

    public void Insert(FarRegionData data, IServerPlayer[] targets)
    {
        sendQueue.Enqueue(new Packet
        {
            RegionData = data,
            Targets = targets,
        });
    }

    public void CancelForTarget(long regionIdx, IServerPlayer target)
    {
        foreach (var packet in sendQueue)
        {
            if (packet.RegionData.RegionIndex == regionIdx)
            {
                packet.Targets = packet.Targets.Where(t => t != target).ToArray();
            }
        }
    }

    public void CancelAllForTarget(IServerPlayer target)
    {
        foreach (var packet in sendQueue)
        {
            packet.Targets = packet.Targets.Where(t => t != target).ToArray();
        }
    }

    public void SendNextBatch()
    {
        if (sendQueue.Count == 0) return;

        var channel = sapi.Network.GetChannel(FarseerModSystem.MOD_CHANNEL_NAME);

        var toSend = new List<Packet>();
        for (int i = 0; i < GameMath.Min(batchSize, sendQueue.Count); i++)
        {
            toSend.Add(sendQueue.Dequeue());
        }

        foreach (var packet in toSend)
        {
            if (packet.Targets.Length > 0)
            {
                channel.SendPacket(packet.RegionData, packet.Targets);
            }
        }
    }
}
