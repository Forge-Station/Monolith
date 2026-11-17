using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>
/// Databases migrated by the earlier company-organization revisions already have these
/// tables, under different migration ids. This branch adds the same schema again, so
/// record that migration as applied instead of creating the tables a second time.
/// </summary>
internal static class CompanyOrganizationMigration
{
    private static readonly string[] Tables =
    [
        "company_bank_accounts",
        "company_bulletins",
        "company_invitations",
        "company_logs",
        "company_relations",
        "company_roles",
    ];

    public static void MarkAppliedIfSchemaExists(DbContext ctx, string migrationId, bool postgres)
    {
        var conn = ctx.Database.GetDbConnection();
        var openedHere = conn.State != ConnectionState.Open;
        if (openedHere)
            conn.Open();

        try
        {
            if (!TableExists(conn, "__EFMigrationsHistory", postgres) || !SchemaExists(conn, postgres))
                return;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                SELECT @id, '10.0.0'
                WHERE NOT EXISTS (
                    SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = @idAgain
                );
                """;
            var id = cmd.CreateParameter();
            id.ParameterName = "@id";
            id.Value = migrationId;
            cmd.Parameters.Add(id);
            var idAgain = cmd.CreateParameter();
            idAgain.ParameterName = "@idAgain";
            idAgain.Value = migrationId;
            cmd.Parameters.Add(idAgain);

            if (cmd.ExecuteNonQuery() > 0)
            {
                Logger.InfoS("db.ef",
                    $"Company tables already exist; recorded migration {migrationId} as applied.");
            }
        }
        finally
        {
            // An in-memory connection is opened once and must stay open, or SQLite wipes it.
            if (openedHere)
                conn.Close();
        }
    }

    private static bool SchemaExists(DbConnection conn, bool postgres)
    {
        foreach (var table in Tables)
        {
            if (!TableExists(conn, table, postgres))
                return false;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = postgres
            ? """
              SELECT COUNT(*) FROM information_schema.columns
              WHERE table_schema = 'public'
                AND table_name = 'company_members'
                AND column_name = 'character_slot'
              """
            : """
              SELECT COUNT(*) FROM pragma_table_info('company_members')
              WHERE name = 'character_slot'
              """;
        return Count(cmd) > 0;
    }

    private static bool TableExists(DbConnection conn, string name, bool postgres)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = postgres
            ? """
              SELECT COUNT(*) FROM information_schema.tables
              WHERE table_schema = 'public' AND table_name = @name
              """
            : """
              SELECT COUNT(*) FROM sqlite_master
              WHERE type = 'table' AND name = @name
              """;
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@name";
        parameter.Value = name;
        cmd.Parameters.Add(parameter);
        return Count(cmd) > 0;
    }

    private static int Count(IDbCommand cmd)
    {
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
}
