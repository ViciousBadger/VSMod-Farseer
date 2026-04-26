using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Farseer.Server;

public class TerrainSamplerAdapter : IDisposable
{
  private readonly FarseerModSystem modSystem;
  private readonly ICoreServerAPI sapi;

  private readonly Func<int, int, int> sampleHeightDelegate;

  private readonly int chunksInRegionColumn;
  private readonly ConcurrentDictionary<long, byte> asyncSamplingInProgress = new();
  private readonly SemaphoreSlim samplerSemaphore;
  private readonly CancellationTokenSource cancelSamplingTokenSource = new();
  private volatile bool disposed;

  public event FarRegionGeneratedDelegate RegionGenerated;

  private TerrainSamplerAdapter(
    FarseerModSystem modSystem,
    ICoreServerAPI serverAPI,
    Func<int, int, int> sampleHeightDelegate,
    int maxConcurrentSampling)
  {
    this.modSystem = modSystem;
    this.sapi = serverAPI;
    this.sampleHeightDelegate = sampleHeightDelegate;
    this.samplerSemaphore = new SemaphoreSlim(maxConcurrentSampling, maxConcurrentSampling);

    this.chunksInRegionColumn = serverAPI.WorldManager.RegionSize / serverAPI.WorldManager.ChunkSize;
  }

  /// <summary>
  /// Creates the Terrain Sampler if the terrain sampler mod is enabled
  /// </summary>
  public static TerrainSamplerAdapter TryCreate(FarseerModSystem modSystem, ICoreServerAPI sapi)
  {
    try
    {
      foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
      {
        Type modType = assembly.GetType("AlgernonsTerrainSampler.TerrainSamplerMod");
        if (modType == null)
          continue;

        PropertyInfo instanceProperty = modType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        MethodInfo getHeightMethod = modType.GetMethod("GetBlockColumnHeight", BindingFlags.Public | BindingFlags.Instance,
          null, [typeof(int), typeof(int)], null);
        if (instanceProperty == null || getHeightMethod == null)
          break;

        object instance = instanceProperty.GetValue(null);
        int sampleHeight(int blockX, int blockZ) => (int)getHeightMethod.Invoke(instance, [blockX, blockZ]);

        int maxConcurrentSampling = Math.Clamp(Environment.ProcessorCount - 4, 1, 4);
        return new TerrainSamplerAdapter(modSystem, sapi, sampleHeight, maxConcurrentSampling);
      }
    }
    catch (Exception) { }

    return null;
  }

  public void StartGeneratingRegion(long regionIdx)
  {
    if (this.disposed || !this.asyncSamplingInProgress.TryAdd(regionIdx, 0))
      return;

    Vec3i regionPos = this.sapi.WorldManager.MapRegionPosFromIndex2D(regionIdx);
    int chunkStartX = regionPos.X * this.chunksInRegionColumn;
    int chunkStartZ = regionPos.Z * this.chunksInRegionColumn;
    int gridSize = this.modSystem.Server.Config.HeightmapGridSize;
    int regionSize = this.sapi.WorldManager.RegionSize;
    int chunkSize = this.sapi.WorldManager.ChunkSize;
    int seaLevel = this.sapi.World.SeaLevel;

    CancellationToken cancelSamplingToken = this.cancelSamplingTokenSource.Token;

    _ = Task.Run(async () =>
    {
      try
      {
        await this.samplerSemaphore.WaitAsync(cancelSamplingToken);
      }
      catch (OperationCanceledException)
      {
        _ = this.asyncSamplingInProgress.TryRemove(regionIdx, out _);
        return;
      }
      catch (ObjectDisposedException)
      {
        _ = this.asyncSamplingInProgress.TryRemove(regionIdx, out _);
        return;
      }

      try
      {
        var heightmap = new FarRegionHeightmap
        {
          GridSize = gridSize,
          Points = new int[gridSize * gridSize],
        };

        float cellSize = regionSize / (float)gridSize;

        for (int z = 0; z < gridSize; z++)
        {
          for (int x = 0; x < gridSize; x++)
          {
            if (cancelSamplingToken.IsCancellationRequested)
            {
              _ = this.asyncSamplingInProgress.TryRemove(regionIdx, out _);
              return;
            }

            int blockX = (chunkStartX * chunkSize) + (int)(x * cellSize);
            int blockZ = (chunkStartZ * chunkSize) + (int)(z * cellSize);

            int sampledHeight = this.sampleHeightDelegate(blockX, blockZ);
            heightmap.Points[(z * gridSize) + x] = GameMath.Max(sampledHeight, seaLevel);
          }
        }

        if (!this.disposed)
        {
          this.sapi.Event.EnqueueMainThreadTask(() =>
          {
            _ = this.asyncSamplingInProgress.TryRemove(regionIdx, out _);
            if (!this.disposed)
              RegionGenerated?.Invoke(regionIdx, heightmap);
          }, "farseer-terrain-sample");
        }
      }
      finally
      {
        try
        {
          _ = this.samplerSemaphore.Release();
        }
        catch (ObjectDisposedException) { }
      }
    });
  }

  public void Dispose()
  {
    if (this.disposed)
      return;

    this.disposed = true;
    this.cancelSamplingTokenSource.Cancel();
    this.cancelSamplingTokenSource.Dispose();
    this.samplerSemaphore.Dispose();
  }
}
