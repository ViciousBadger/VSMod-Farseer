namespace Farseer;

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

public class FarDB : SQLiteDBConnection
{
    public FarDB(ILogger logger) : base(logger)
    {
    }

    SqliteCommand getRegionCmd;
    SqliteCommand insertRegionCmd;

    public override void OnOpened()
    {
        base.OnOpened();

        getRegionCmd = sqliteConn.CreateCommand();
        getRegionCmd.CommandText = "SELECT heightmap FROM region WHERE position=@pos";
        getRegionCmd.Parameters.Add("@pos", SqliteType.Integer, 1);
        getRegionCmd.Prepare();

        insertRegionCmd = sqliteConn.CreateCommand();
        insertRegionCmd.CommandText = "INSERT OR REPLACE INTO region (position, heightmap) VALUES (@pos, @heightmap)";
        insertRegionCmd.Parameters.Add("@pos", SqliteType.Integer, 1);
        insertRegionCmd.Parameters.Add("@heightmap", SqliteType.Blob);
        insertRegionCmd.Prepare();
    }

    protected override void CreateTablesIfNotExists(SqliteConnection sqliteConn)
    {
        using (var cmd = sqliteConn.CreateCommand())
        {
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS region (position integer PRIMARY KEY, heightmap BLOB)";
            cmd.ExecuteNonQuery();
        }
    }

    public FarRegionHeightmap GetRegionHeightmap(long regionIdx)
    {
        getRegionCmd.Parameters["@pos"].Value = regionIdx;
        using var reader = getRegionCmd.ExecuteReader();
        if (reader.Read())
        {
            var heightmap = reader["heightmap"];
            if (heightmap == null) return null;
            return SerializerUtil.Deserialize<FarRegionHeightmap>(heightmap as byte[]);
        }
        else
        {
            return null;
        }
    }

    public void InsertRegionHeightmap(long regionIdx, FarRegionHeightmap heightmap)
    {
        using var transaction = sqliteConn.BeginTransaction();
        insertRegionCmd.Transaction = transaction;

        insertRegionCmd.Parameters["@pos"].Value = regionIdx;
        insertRegionCmd.Parameters["@heightmap"].Value = SerializerUtil.Serialize(heightmap);
        insertRegionCmd.ExecuteNonQuery();

        transaction.Commit();
    }

    public override void Close()
    {
        insertRegionCmd?.Dispose();
        getRegionCmd?.Dispose();

        base.Close();
    }

    public override void Dispose()
    {
        insertRegionCmd?.Dispose();
        getRegionCmd?.Dispose();

        base.Dispose();
    }

}
