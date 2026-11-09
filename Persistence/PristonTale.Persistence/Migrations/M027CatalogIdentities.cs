using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// Monsters and NPCs are keyed by the importer's stable numeric id and named by a unique English key; every catalog
// reference to them (drops, services, shop stock, spawns, bosses, placements) holds the id. Labels and dialog lines
// keep the authored text of each name and dialog key. The catalog tables are rebuilt empty: import again afterwards.
[Migration(27)]
public sealed class M027CatalogIdentities : Migration
{
    private static readonly string[] Elements =
        ["bionic", "earth", "fire", "ice", "lighting", "poison", "water", "wind"];

    private static readonly string[] DroppedTables =
    [
        "field_fixed_npcs", "field_boss_spawns", "field_monster_spawns", "npc_shop_stock", "npc_services", "npcs",
        "monster_drops_plus", "monster_drop_items", "monster_drops", "monsters"
    ];

    public override void Up()
    {
        foreach (var table in DroppedTables)
        {
            Delete.Table(table);
        }

        Delete.FromTable("world_entity_variants").AllRows();
        Delete.FromTable("world_import_unresolved_refs").AllRows();
        CreateMonsters();
        CreateNpcs();
        CreateFieldReferences();
        CreateLabels("item_labels", "item_code", "item_templates", "code", OwnerKey.Code);
    }

    public override void Down() =>
        throw new NotSupportedException("M027CatalogIdentities rebuilds the world catalog; restore a backup to go back.");

    private enum OwnerKey
    {
        Number,
        Code
    }

    private void CreateMonsters()
    {
        var table = Create.Table("monsters")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("key").AsString(128).NotNullable().Unique()
            .WithColumn("display_name").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_speed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("accuracy").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("block_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("hp").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("sight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_damage_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_damage_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_distance").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_rating_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("skill_curse_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_speed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("iq").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("group_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("group_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("exp").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("active_hour").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("nature").AsInt16().NotNullable().WithDefaultValue(0x80)
            .WithColumn("brood").AsInt16().NotNullable().WithDefaultValue(0)
            .WithColumn("class").AsInt16().NotNullable().WithDefaultValue(0);

        foreach (var element in Elements)
        {
            table = table.WithColumn($"res_{element}").AsInt32().NotNullable().WithDefaultValue(0);
        }

        table
            .WithColumn("stun_pct").AsInt32().NotNullable().WithDefaultValue(100)
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1)
            .WithColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("quest_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_item").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("potion_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("potion_percent").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .WithColumn("res_magic").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_type").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("special_attack_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_pct").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("event_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("event_info").AsInt32().NotNullable().WithDefaultValue(0);

        CreateLabels("monster_labels", "monster_id", "monsters", "id", OwnerKey.Number);

        Create.Table("monster_drops")
            .WithColumn("monster_id").AsInt32().PrimaryKey()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("max_items_per_kill").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("all_players_see").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Table("monster_drop_items")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("monster_id").AsInt32().NotNullable()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).Nullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Index("monster_drop_items_monster_idx").OnTable("monster_drop_items").OnColumn("monster_id");

        Create.Table("monster_drops_plus")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("monster_id").AsInt32().NotNullable()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).Nullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.SetNull);
        Create.Index("monster_drops_plus_monster_idx").OnTable("monster_drops_plus").OnColumn("monster_id");
    }

    private void CreateNpcs()
    {
        Create.Table("npcs")
            .WithColumn("id").AsInt32().PrimaryKey()
            .WithColumn("key").AsString(128).NotNullable().Unique()
            .WithColumn("display_name").AsString(128).NotNullable().WithDefaultValue("")
            .WithColumn("model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("linked_file").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("dialog_key").AsString(16).NotNullable().WithDefaultValue("")
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_range").AsInt32().NotNullable().WithDefaultValue(64)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1)
            .WithColumn("portrait_offset_y").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("portrait_offset_z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("hostile").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(-1)
            .WithColumn("appear_slot").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("appear_cycle").AsInt32().NotNullable().WithDefaultValue(0);

        CreateLabels("npc_labels", "npc_id", "npcs", "id", OwnerKey.Number);

        Create.Table("npc_dialog_lines")
            .WithColumn("npc_id").AsInt32().NotNullable().PrimaryKey()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("source_key").AsString(16).NotNullable().PrimaryKey()
            .WithColumn("ordinal").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("text").AsString().NotNullable();

        Create.Table("npc_services")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("npc_id").AsInt32().NotNullable()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("value").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("value2").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("text").AsString().NotNullable().WithDefaultValue("");
        Create.Index("npc_services_npc_idx").OnTable("npc_services").OnColumn("npc_id");

        Create.Table("npc_shop_stock")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("npc_id").AsInt32().NotNullable()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("tab").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).NotNullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.Cascade);
        Create.Index("npc_shop_stock_npc_idx").OnTable("npc_shop_stock").OnColumn("npc_id");
    }

    private void CreateFieldReferences()
    {
        Create.Table("field_monster_spawns")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("monster_id").AsInt32().NotNullable()
                .ForeignKey("monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Index("field_monster_spawns_field_idx").OnTable("field_monster_spawns").OnColumn("field_code");

        Create.Table("field_boss_spawns")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("boss_monster_id").AsInt32().NotNullable()
                .ForeignKey("field_boss_spawns_boss_fk", "monsters", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("escort_monster_id").AsInt32().Nullable()
                .ForeignKey("field_boss_spawns_escort_fk", "monsters", "id").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("escort_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("spawn_hours").AsString().NotNullable().WithDefaultValue("");
        Create.Index("field_boss_spawns_field_idx").OnTable("field_boss_spawns").OnColumn("field_code");

        Create.Table("field_fixed_npcs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("npc_id").AsInt32().NotNullable()
                .ForeignKey("npcs", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("x").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("angle").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("y").AsInt32().NotNullable().WithDefaultValue(0);
        Create.Index("field_fixed_npcs_field_idx").OnTable("field_fixed_npcs").OnColumn("field_code");
    }

    // One authored text per name key of an entity (the key upper-case without its asterisk: NAME, E_NAME, B_NAME...).
    private void CreateLabels(string table, string ownerColumn, string ownerTable, string ownerKeyColumn, OwnerKey ownerType)
    {
        var owner = Create.Table(table).WithColumn(ownerColumn);
        var typed = ownerType == OwnerKey.Number ? owner.AsInt32() : owner.AsString(16);
        typed.NotNullable().PrimaryKey().ForeignKey(ownerTable, ownerKeyColumn).OnDelete(System.Data.Rule.Cascade)
            .WithColumn("source_key").AsString(16).NotNullable().PrimaryKey()
            .WithColumn("text").AsString().NotNullable();
    }
}
