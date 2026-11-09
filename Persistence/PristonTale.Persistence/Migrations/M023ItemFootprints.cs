using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Item templates carry their inventory footprint, measured by the importer from each item's inventory sprite. Re-import the
// world data afterwards to fill it.
[Migration(23)]
public sealed class M023ItemFootprints : Migration
{
    public override void Up()
    {
        Alter.Table("item_templates")
            .AddColumn("grid_width").AsInt32().NotNullable().WithDefaultValue(1)
            .AddColumn("grid_height").AsInt32().NotNullable().WithDefaultValue(1);
    }

    public override void Down()
    {
        Delete.Column("grid_height").FromTable("item_templates");
        Delete.Column("grid_width").FromTable("item_templates");
    }
}
