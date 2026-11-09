using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// A field NPC placement keeps where the NPC stands: the class and shadow size its record carries belong to the NPC file
// that lacks them, and the importer gives them to it. Import again afterwards.
[Migration(29)]
public sealed class M029NpcPlacementFacts : Migration
{
    public override void Up()
    {
        // Both providers drop a plain column in place (SQLite from 3.35).
        Execute.Sql("ALTER TABLE field_npcs DROP COLUMN quest_trigger");
        Execute.Sql("ALTER TABLE field_npcs DROP COLUMN size_level");
    }

    public override void Down() =>
        Alter.Table("field_npcs")
            .AddColumn("quest_trigger").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("size_level").AsInt32().NotNullable().WithDefaultValue(0);
}
