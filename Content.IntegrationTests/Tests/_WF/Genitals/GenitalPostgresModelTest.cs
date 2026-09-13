using Content.Server.Database;
using Microsoft.EntityFrameworkCore;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>The Postgres model snapshot matches the model. ServerDbSqliteTests.TestNoPendingDatabaseChanges covers Sqlite only.</summary>
/// <remarks>HasPendingModelChanges compares against the snapshot without opening a connection, so no database is needed.</remarks>
[TestFixture]
[TestOf(typeof(PostgresServerDbContext))]
public sealed class GenitalPostgresModelTest
{
    [Test]
    public void NoPendingPostgresModelChangesTest()
    {
        var builder = new DbContextOptionsBuilder<PostgresServerDbContext>();
        builder.UseNpgsql("Host=unused");
        using var context = new PostgresServerDbContext(builder.Options);

        Assert.That(context.Database.HasPendingModelChanges(), Is.False,
            "The Postgres model has changes without a migration. Run Content.Server.Database/add-migration.sh.");
    }
}
