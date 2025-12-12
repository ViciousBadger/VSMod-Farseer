using HarmonyLib;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Farseer;

/// <summary>
/// Harmony-assisted batch peek helper that keeps all generation on the vanilla chunk thread.
/// This lets us reduce pause/resume overhead without running worldgen on our own threads.
/// </summary>
public static class BatchPeekPatch
{
    private class BatchRequest
    {
        public List<Vec2i> Coords;
        public EnumWorldGenPass UntilPass;
        public ITreeAttribute ChunkGenParams;
        public Action<Dictionary<Vec2i, IServerChunk[]>> Callback;
    }

    private static readonly ConcurrentQueue<BatchRequest> queue = new();
    private static readonly object patchLock = new();

    private static Harmony harmony;
    private static bool patched;
    private static int maxColumnsPerTick = 96;

    private static MethodInfo pauseAllWorldgenThreads;
    private static MethodInfo resumeAllWorldgenThreads;
    private static MethodInfo peekChunkAreaLocking;

    public static void Configure(int maxColumns)
    {
        maxColumnsPerTick = Math.Clamp(maxColumns, 16, 512);
    }

    public static void Apply()
    {
        lock (patchLock)
        {
            if (patched) return;

            harmony = new Harmony("farseer.batchpeek");
            var target = AccessTools.Method("Vintagestory.Server.ServerSystemSupplyChunks:OnSeparateThreadTick");
            if (target != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(BatchPeekPatch), nameof(OnSeparateThreadTickPostfix)));
                patched = true;
            }
        }
    }

    /// <summary>
    /// Enqueue a batch of chunk coordinates to be peeked on the chunk thread.
    /// Returns false if the patch is not active.
    /// </summary>
    public static bool Enqueue(List<Vec2i> coords, EnumWorldGenPass untilPass, ITreeAttribute chunkGenParams, Action<Dictionary<Vec2i, IServerChunk[]>> callback)
    {
        if (!patched || coords == null || coords.Count == 0) return false;

        queue.Enqueue(new BatchRequest
        {
            Coords = coords,
            UntilPass = untilPass,
            ChunkGenParams = chunkGenParams ?? new TreeAttribute(),
            Callback = callback
        });

        return true;
    }

    private static void OnSeparateThreadTickPostfix(object __instance)
    {
        if (queue.IsEmpty) return;
        TryWarmReflection(__instance);
        if (pauseAllWorldgenThreads == null || resumeAllWorldgenThreads == null || peekChunkAreaLocking == null) return;

        var supply = __instance;

        if (!(bool)pauseAllWorldgenThreads.Invoke(supply, new object[] { 3600 }))
        {
            return;
        }

        try
        {
            int consumed = 0;
            while (consumed < maxColumnsPerTick && queue.TryDequeue(out var req))
            {
                if (req.Coords == null || req.Coords.Count == 0) continue;

                var remaining = req.Coords.Count;
                var combined = new Dictionary<Vec2i, IServerChunk[]>(req.Coords.Count);

                foreach (var coord in req.Coords)
                {
                    OnChunkPeekedDelegate onGenerated = (Dictionary<Vec2i, IServerChunk[]> dict) =>
                    {
                        lock (combined)
                        {
                            foreach (var pair in dict)
                            {
                                combined[pair.Key] = pair.Value;
                            }
                        }

                        if (Interlocked.Decrement(ref remaining) == 0)
                        {
                            req.Callback?.Invoke(new Dictionary<Vec2i, IServerChunk[]>(combined));
                        }
                    };

                    peekChunkAreaLocking.Invoke(supply, new object[]
                    {
                        coord,
                        req.UntilPass,
                        onGenerated,
                        req.ChunkGenParams
                    });
                }

                consumed += req.Coords.Count;
            }
        }
        finally
        {
            resumeAllWorldgenThreads.Invoke(supply, Array.Empty<object>());
        }
    }

    private static void TryWarmReflection(object supply)
    {
        if (pauseAllWorldgenThreads != null && resumeAllWorldgenThreads != null && peekChunkAreaLocking != null) return;

        var t = supply.GetType();
        pauseAllWorldgenThreads = AccessTools.Method(t, "PauseAllWorldgenThreads");
        resumeAllWorldgenThreads = AccessTools.Method(t, "ResumeAllWorldgenThreads");
        peekChunkAreaLocking = AccessTools.Method(t, "PeekChunkAreaLocking");
    }
}
