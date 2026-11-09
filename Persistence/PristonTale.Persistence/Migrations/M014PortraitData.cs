using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Target-frame data of the character files: the authored portrait camera correction (height, then distance) of
// monsters and NPCs, and the NPC display name. Columns append in record constructor order.
[Migration(14)]
public sealed class M014PortraitData : Migration
{
    public override void Up()
    {
        Alter.Table("monsters")
            .AddColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0);
        Alter.Table("npcs")
            .AddColumn("display_name").AsString(128).NotNullable().WithDefaultValue("")
            .AddColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("portrait_offset_z").FromTable("npcs");
        Delete.Column("portrait_offset_y").FromTable("npcs");
        Delete.Column("display_name").FromTable("npcs");
        Delete.Column("portrait_offset_z").FromTable("monsters");
        Delete.Column("portrait_offset_y").FromTable("monsters");
    }
}
