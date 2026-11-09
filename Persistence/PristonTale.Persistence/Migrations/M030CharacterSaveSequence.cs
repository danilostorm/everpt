using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The save sequence of a character's stored capture. Background saves may reach storage out of order, and a write never
// replaces a newer capture (DapperCharacterRepository.UpdateSql).
[Migration(30)]
public sealed class M030CharacterSaveSequence : Migration
{
    public override void Up() => Alter.Table("characters").AddColumn("save_seq").AsInt64().NotNullable().WithDefaultValue(0);

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down() => Execute.Sql("ALTER TABLE characters DROP COLUMN save_seq");
}
