using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(25)]
public sealed class M025ServerSettings : Migration
{
    public override void Up()
    {
        Create.Table("server_settings")
            .WithColumn("name").AsString().PrimaryKey()
            .WithColumn("value").AsString().NotNullable()
            .WithColumn("description").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("updated_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);
    }

    public override void Down()
    {
        Delete.Table("server_settings");
    }
}
