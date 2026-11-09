using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(26)]
public sealed class M026Contacts : Migration
{
    public override void Up()
    {
        Create.Table("character_contacts")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("character_id").AsInt64().NotNullable()
                .ForeignKey("characters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("list").AsInt16().NotNullable()
            .WithColumn("name").AsString().NotNullable();
        Create.Index("ix_character_contacts_entry").OnTable("character_contacts")
            .OnColumn("character_id").Ascending()
            .OnColumn("list").Ascending()
            .OnColumn("name").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("character_contacts");
    }
}
