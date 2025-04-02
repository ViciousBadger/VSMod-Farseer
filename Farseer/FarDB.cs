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
        int heightmapSize = 16 * 16;
        var heightmap = new int[heightmapSize];
        var region = new FarRegionHeightmap
        {
            Resolution = 16,
            Heightmap = heightmap
        };

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                heightmap[z * 16 + x] = x + z;
            }
        }
        return region;
    }

}
