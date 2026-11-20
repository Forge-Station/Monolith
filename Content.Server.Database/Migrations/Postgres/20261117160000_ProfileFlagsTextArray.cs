using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <summary>
    /// <see cref="WolfgateGameScore"/> shipped <c>profile.flags</c> as <c>text</c> with default <c>'[]'</c>.
    /// The model reads it as <c>text[]</c>, so loading preferences threw and the server kicked the player.
    /// Databases that already applied the old migration need the column rewritten. Fresh databases create
    /// <c>text[]</c> directly, and this step leaves them alone.
    /// </summary>
    [DbContext(typeof(Content.Server.Database.PostgresServerDbContext))]
    [Migration("20261117160000_ProfileFlagsTextArray")]
    public class ProfileFlagsTextArray : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'profile'
                          AND column_name = 'flags'
                          AND udt_name = 'text'
                    ) THEN
                        ALTER TABLE profile ALTER COLUMN flags DROP DEFAULT;
                        ALTER TABLE profile
                            ALTER COLUMN flags TYPE text[]
                            USING (
                                CASE
                                    WHEN flags IS NULL OR btrim(flags) IN ('', '[]', '{}') THEN ARRAY[]::text[]
                                    WHEN left(btrim(flags), 1) = '{' THEN flags::text[]
                                    ELSE ARRAY[]::text[]
                                END
                            );
                        ALTER TABLE profile ALTER COLUMN flags SET DEFAULT ARRAY[]::text[];
                        ALTER TABLE profile ALTER COLUMN flags SET NOT NULL;
                    END IF;
                END $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'profile'
                          AND column_name = 'flags'
                          AND udt_name = '_text'
                    ) THEN
                        ALTER TABLE profile ALTER COLUMN flags DROP DEFAULT;
                        ALTER TABLE profile
                            ALTER COLUMN flags TYPE text
                            USING (
                                CASE
                                    WHEN flags IS NULL OR flags = ARRAY[]::text[] THEN '[]'
                                    ELSE flags::text
                                END
                            );
                        ALTER TABLE profile ALTER COLUMN flags SET DEFAULT '[]';
                        ALTER TABLE profile ALTER COLUMN flags SET NOT NULL;
                    END IF;
                END $$;
                """);
        }
    }
}
