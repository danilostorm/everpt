using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The game logs: rows only ever inserted (by a background writer) and pruned by age (Logs:RetentionDays). Item transfers
// are the items and gold that moved between characters, the ground, an account warehouse or a game master; currency
// sinks the gold that left the economy in each period; server events the starts and stops. No foreign keys: a log
// outlives the characters, accounts and items it names.
[Migration(31)]
public sealed class M031GameLogs : Migration
{
    public override void Up()
    {
        Create.Table("item_transfers")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("occurred_at").AsDateTime().NotNullable()
            .WithColumn("kind").AsInt16().NotNullable()
            .WithColumn("transfer_id").AsString(36).NotNullable()
            .WithColumn("from_character_id").AsInt64().Nullable()
            .WithColumn("to_character_id").AsInt64().Nullable()
            .WithColumn("account_id").AsInt64().Nullable()
            .WithColumn("field_id").AsInt32().NotNullable()
            .WithColumn("item_code").AsString(16).Nullable()
            .WithColumn("instance_id").AsString(36).Nullable()
            .WithColumn("amount").AsInt64().NotNullable();
        Create.Index("item_transfers_occurred_idx").OnTable("item_transfers").OnColumn("occurred_at");
        Create.Index("item_transfers_from_idx").OnTable("item_transfers").OnColumn("from_character_id");
        Create.Index("item_transfers_to_idx").OnTable("item_transfers").OnColumn("to_character_id");
        Create.Index("item_transfers_instance_idx").OnTable("item_transfers").OnColumn("instance_id");

        Create.Table("currency_sinks")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("occurred_at").AsDateTime().NotNullable()
            .WithColumn("sink").AsInt16().NotNullable()
            .WithColumn("gold").AsInt64().NotNullable();
        Create.Index("currency_sinks_occurred_idx").OnTable("currency_sinks").OnColumn("occurred_at");

        Create.Table("server_events")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("occurred_at").AsDateTime().NotNullable()
            .WithColumn("kind").AsInt16().NotNullable()
            .WithColumn("server_name").AsString(64).NotNullable()
            .WithColumn("detail").AsString().NotNullable().WithDefaultValue("");
        Create.Index("server_events_occurred_idx").OnTable("server_events").OnColumn("occurred_at");
    }

    public override void Down()
    {
        Delete.Table("server_events");
        Delete.Table("currency_sinks");
        Delete.Table("item_transfers");
    }
}
