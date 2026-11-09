using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Account game-master levels (0 = player) and the audit trail of every game-master command a player typed. The audit
// keeps the account id without a foreign key so the trail survives a deleted account.
[Migration(19)]
public sealed class M019GameMasters : Migration
{
    public override void Up()
    {
        Alter.Table("accounts")
            .AddColumn("gm_level").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("gm_audit")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("account_id").AsInt64().NotNullable()
            .WithColumn("user_id").AsString().NotNullable()
            .WithColumn("character_name").AsString().NotNullable()
            .WithColumn("gm_level").AsInt32().NotNullable()
            .WithColumn("command").AsString().NotNullable()
            .WithColumn("required_level").AsInt32().NotNullable()
            .WithColumn("accepted").AsBoolean().NotNullable()
            .WithColumn("result").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime);

        Create.Index("gm_audit_account_idx").OnTable("gm_audit").OnColumn("account_id");
    }

    public override void Down()
    {
        Delete.Table("gm_audit");
        Delete.Column("gm_level").FromTable("accounts");
    }
}
