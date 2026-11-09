using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// How many of its starter gifts a character has received. Characters stored before count as served: the default is the
// five gifts there were when the column came (a later gift would reach them unless its own migration says otherwise);
// a new character starts at 0 (CharacterRecordFactory.NewStarter).
[Migration(32)]
public sealed class M032StarterGifts : Migration
{
    public override void Up() =>
        Alter.Table("characters").AddColumn("starter_gifts_received").AsInt32().NotNullable().WithDefaultValue(5);

    // Both providers drop a plain column in place (SQLite from 3.35).
    public override void Down() => Execute.Sql("ALTER TABLE characters DROP COLUMN starter_gifts_received");
}
