using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Every key of the world data files is imported: monsters carry whether they are hostile, their shadow size, magic
// resistance, movement type, special attack chance, death gold, event model and event creature code; NPCs whether they
// are hostile, their level, shadow size and appearance draw; items the glow of unique weapons, their effect setting,
// their daily drop limit and their set; item_sets holds each set's bonus. Re-import the world data afterwards.
[Migration(24)]
public sealed class M024WorldDataFields : Migration
{
    public override void Up()
    {
        Alter.Table("monsters")
            .AddColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(true)
            .AddColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .AddColumn("res_magic").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("move_type").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("special_attack_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("gold_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("event_model").AsString().NotNullable().WithDefaultValue("")
            .AddColumn("event_code").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("event_info").AsInt32().NotNullable().WithDefaultValue(0);
        Alter.Table("npcs")
            .AddColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(false)
            .AddColumn("level").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .AddColumn("appear_slot").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("appear_cycle").AsInt32().NotNullable().WithDefaultValue(0);
        Alter.Table("item_templates")
            .AddColumn("glow_r").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("glow_g").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("glow_b").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("glow_a").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("glow_blink").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("glow_scale").AsFloat().NotNullable().WithDefaultValue(0)
            .AddColumn("display_effect").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("daily_drop_limit").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("set_code").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("set_pieces").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Table("item_sets")
            .WithColumn("set_code").AsInt32().PrimaryKey()
            .WithColumn("pieces").AsInt32().NotNullable()
            .WithColumn("life").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("block").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("description").AsString().NotNullable().WithDefaultValue("");
    }

    public override void Down()
    {
        Delete.Table("item_sets");
        foreach (var column in new[] { "set_pieces", "set_code", "daily_drop_limit", "display_effect", "glow_scale", "glow_blink", "glow_a", "glow_b",
                     "glow_g", "glow_r" })
        {
            Delete.Column(column).FromTable("item_templates");
        }

        foreach (var column in new[] { "appear_cycle", "appear_slot", "size_level", "level", "hostile" })
        {
            Delete.Column(column).FromTable("npcs");
        }

        foreach (var column in new[] { "event_info", "event_code", "event_model", "gold_pct", "gold_max", "gold_min", "special_attack_pct", "move_type",
                     "res_magic", "size_level", "hostile" })
        {
            Delete.Column(column).FromTable("monsters");
        }
    }
}
