using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Monster experience is 64-bit: some bosses give more than 2^31. SQLite integers are already 64-bit; PostgreSQL widens
// the column. Re-import the world data afterwards to replace the values that wrapped in the 32-bit column.
[Migration(22)]
public sealed class M022MonsterExperience64 : Migration
{
    public override void Up()
    {
        IfDatabase("PostgreSQL").Execute.Sql("ALTER TABLE monsters ALTER COLUMN exp TYPE bigint;");
    }

    public override void Down()
    {
        IfDatabase("PostgreSQL").Execute.Sql("ALTER TABLE monsters ALTER COLUMN exp TYPE integer;");
    }
}
