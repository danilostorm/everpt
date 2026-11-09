using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Monster facts quests depend on: the classification code quests hunt by, the item handed out on every kill and the
// potions the monster carries. Quest variants of items reuse item_templates under their own key, and the quest journal
// already has the characters.quests column.
[Migration(16)]
public sealed class M016QuestData : Migration
{
    public override void Up()
    {
        Alter.Table("monsters")
            .AddColumn("quest_code").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("event_item").AsString().NotNullable().WithDefaultValue("")
            .AddColumn("potion_count").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("potion_percent").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("potion_percent").FromTable("monsters");
        Delete.Column("potion_count").FromTable("monsters");
        Delete.Column("event_item").FromTable("monsters");
        Delete.Column("quest_code").FromTable("monsters");
    }
}
