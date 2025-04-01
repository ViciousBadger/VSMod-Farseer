namespace Seefar;

using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;

public class TerrainDB : SQLiteDBConnection
{
    public TerrainDB(ILogger logger) : base(logger)
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

    public void SetRegion(
}
