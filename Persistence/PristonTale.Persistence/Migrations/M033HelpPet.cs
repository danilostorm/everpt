using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The beginner's helper pet of a character: its kind (0 until one is drawn) and whether the player hid it.
[Migration(33)]
public sealed class M033HelpPet : Migration
{
    public override void Up()
    {
        Alter.Table("characters").AddColumn("help_pet").AsInt16().NotNullable().WithDefaultValue(0);
        Alter.Table("characters").AddColumn("help_pet_hidden").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down()
    {
        Execute.Sql("ALTER TABLE characters DROP COLUMN help_pet_hidden");
        Execute.Sql("ALTER TABLE characters DROP COLUMN help_pet");
    }
}
