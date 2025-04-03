namespace Farseer;

using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;

public class FarDB : SQLiteDBConnection
{
    public FarDB(ILogger logger) : base(logger)
    {
    }

    public override void OnOpened()
    {
        base.OnOpened();
    }

    protected override void CreateTablesIfNotExists(SqliteConnection sqliteConn)
    {
        using (var cmd = sqliteConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS region (position integer PRIMARY KEY, heightmap BLOB)";
            cmd.ExecuteNonQuery();
        }
    }

    public FarRegionHeightmap GetFarRegion(long regionIndex)
    {
        int gridSize = 32;
        int heightmapSize = gridSize * gridSize;
        var heightmap = new int[heightmapSize];
        var region = new FarRegionHeightmap
        {
            GridSize = gridSize,
            Points = heightmap
        };

        for (int x = 0; x < gridSize; x++)
        {
            for (int z = 0; z < gridSize; z++)
            {
                heightmap[z * gridSize + x] = 200 + x + z;
            }
        }
        return region;
    }

}
