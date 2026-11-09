using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Typed NPC services and the revision that serializes account warehouse writes. The NPC display name may already exist
// from the portrait migration; it is added only when missing.
[Migration(15)]
public sealed class M015NpcServices : Migration
{
    public override void Up()
    {
        if (!Schema.Table("npcs").Column("display_name").Exists())
        {
            Alter.Table("npcs")
                .AddColumn("display_name").AsString().NotNullable().WithDefaultValue("");
        }

        Alter.Table("warehouses")
            .AddColumn("revision").AsInt64().NotNullable().WithDefaultValue(0);

        Create.Table("npc_services")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("npc_name").AsString().NotNullable()
                .ForeignKey("npcs", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("value").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("value2").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("text").AsString().NotNullable().WithDefaultValue("");

        Create.Index("npc_services_npc_idx").OnTable("npc_services").OnColumn("npc_name");
    }

    public override void Down()
    {
        Delete.Table("npc_services");
        Delete.Column("revision").FromTable("warehouses");
    }
}
