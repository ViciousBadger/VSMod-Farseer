using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.IO.Compression;
using Vintagestory.API.Server;
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
    private FarseerModSystem modSystem;
    private ICoreServerAPI sapi;

    public BatchedRegionDataBuffer(FarseerModSystem modSystem, ICoreServerAPI sapi, int batchSize)
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
        var profiler = sapi.World.FrameProfiler;
        bool profile = profiler?.Enabled == true;
        if (profile) profiler.Enter("farseer-sendbatch");

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
                var prepared = PreparePacket(packet.RegionData, packet.Targets);
                channel.SendPacket(prepared, packet.Targets);
            }
        }

        if (profile) profiler.Leave();
    }

    private FarRegionData PreparePacket(FarRegionData source, IServerPlayer[] targets)
    {
        // If compression disabled, send as-is
        if (!modSystem.Server.Config.EnableCompression)
        {
            return source;
        }

        // If any target lacks capability, send uncompressed
        foreach (var t in targets)
        {
            if (!modSystem.Server.IsCompressionCapable(t))
            {
                return source;
            }
        }

        int threshold = modSystem.Server.Config.CompressionThresholdBytes;

        // Create a lightweight copy so we can null raw arrays for transmission without mutating cache
        var copy = new FarRegionData
        {
            RegionIndex = source.RegionIndex,
            RegionX = source.RegionX,
            RegionZ = source.RegionZ,
            RegionSize = source.RegionSize,
            RegionMapSize = source.RegionMapSize,
            Heightmap = new FarRegionHeightmap
            {
                GridSize = source.Heightmap.GridSize,
                Points = source.Heightmap.Points,
                Colors = source.Heightmap.Colors
            }
        };

        // Compress points
        if (copy.Heightmap.Points != null)
        {
            var rawBytes = IntArrayToBytes(copy.Heightmap.Points);
            if (rawBytes.Length >= threshold)
            {
                copy.CompressedPoints = Deflate(rawBytes);
                copy.Heightmap.Points = null;
                copy.Compressed = true;
            }
        }

        // Compress colors if present
        if (copy.Heightmap.Colors != null)
        {
            var rawBytes = IntArrayToBytes(copy.Heightmap.Colors);
            if (rawBytes.Length >= threshold)
            {
                copy.CompressedColors = Deflate(rawBytes);
                copy.Heightmap.Colors = null;
                copy.Compressed = true;
            }
        }

        return copy;
    }

    private byte[] IntArrayToBytes(int[] arr)
    {
        var bytes = new byte[arr.Length * sizeof(int)];
        System.Buffer.BlockCopy(arr, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private byte[] Deflate(byte[] data)
    {
        using var ms = new System.IO.MemoryStream();
        using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Fastest, true))
        {
            ds.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }
}
