using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

[Migration(13)]
public sealed class M013Baseline : Migration
{
    private static readonly string[] Elements =
        ["bionic", "earth", "fire", "ice", "lighting", "poison", "water", "wind"];

    private static readonly (string Table, string Column, string Default)[] JsonColumns =
    [
        ("characters", "inventory", "[]"),
        ("characters", "equipment", "{}"),
        ("characters", "skills", "[]"),
        ("characters", "quests", "{}"),
        ("characters", "derived_stats", "{}"),
        ("characters", "premium", "{}"),
        ("warehouses", "items", "[]"),
        ("mailboxes", "items", "[]"),
        ("npcs", "dialog", "[]"),
    ];

    public override void Up()
    {
        if (Schema.Table("accounts").Exists())
        {
            Execute.WithConnection(VerifyEarlierBaseline);
            Alter.Table("item_templates")
                .AddColumn("max_stack").AsInt32().NotNullable().WithDefaultValue(1)
                .AddColumn("potion_space").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("magic_mastery").AsFloat().NotNullable().WithDefaultValue(0);
            Alter.Table("monsters")
                .AddColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
                .AddColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1);
            Alter.Table("npcs")
                .AddColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1);
            CreateSourceFiles();
            CreateEntityVariants();
            CreateUnresolvedReferences();
            return;
        }

        Create.Table("accounts")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("user_id").AsString().NotNullable().Unique()
            .WithColumn("password_hash").AsString().NotNullable()
            .WithColumn("is_blocked").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("is_tester").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("last_login_at").AsDateTime().Nullable();

        Create.Table("characters")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("account_id").AsInt64().NotNullable()
                .ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("name").AsString().NotNullable().Unique()
            .WithColumn("slot").AsInt16().NotNullable()

            .WithColumn("job_code").AsInt32().NotNullable()
            .WithColumn("level").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("exp").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("money").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("brood").AsInt32().NotNullable().WithDefaultValue(0)

            .WithColumn("strength").AsInt32().NotNullable()
            .WithColumn("spirit").AsInt32().NotNullable()
            .WithColumn("talent").AsInt32().NotNullable()
            .WithColumn("agility").AsInt32().NotNullable()
            .WithColumn("health").AsInt32().NotNullable()
            .WithColumn("stat_points").AsInt32().NotNullable().WithDefaultValue(0)

            .WithColumn("life").AsInt32().NotNullable()
            .WithColumn("mana").AsInt32().NotNullable()
            .WithColumn("stamina").AsInt32().NotNullable()

            .WithColumn("field_id").AsInt32().NotNullable()
            .WithColumn("pos_x").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("pos_z").AsFloat().NotNullable().WithDefaultValue(0)

            .WithColumn("model_name").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("model_name2").AsString().NotNullable().WithDefaultValue("")

            .WithColumn("inventory").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("equipment").AsString().NotNullable().WithDefaultValue("{}")
            .WithColumn("skills").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("quests").AsString().NotNullable().WithDefaultValue("{}")
            .WithColumn("derived_stats").AsString().NotNullable().WithDefaultValue("{}")
            .WithColumn("premium").AsString().NotNullable().WithDefaultValue("{}")

            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentUTCDateTime)
            .WithColumn("last_played_at").AsDateTime().Nullable()
            .WithColumn("face_index").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("tier").AsInt16().NotNullable().WithDefaultValue(0)
            .WithColumn("hair_index").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("size_level").AsInt32().NotNullable().WithDefaultValue(0);

        Create.UniqueConstraint().OnTable("characters").Columns("account_id", "slot");
        Create.Index("characters_account_id_idx").OnTable("characters").OnColumn("account_id");

        Create.Table("warehouses")
            .WithColumn("account_id").AsInt64().PrimaryKey()
                .ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("money").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("buy_area_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("items").AsString().NotNullable().WithDefaultValue("[]");

        Create.Table("mailboxes")
            .WithColumn("account_id").AsInt64().PrimaryKey()
                .ForeignKey("accounts", "id").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("items").AsString().NotNullable().WithDefaultValue("[]");

        CreateItemTemplates();
        CreateMonsters();
        CreateNpcs();
        CreateFields();
        CreateMotionTiming();
        CreateSourceFiles();
        CreateEntityVariants();
        CreateUnresolvedReferences();

        foreach (var (table, column, def) in JsonColumns)
        {
            IfDatabase("PostgreSQL").Execute.Sql(
                $"ALTER TABLE {table} " +
                $"ALTER COLUMN {column} DROP DEFAULT, " +
                $"ALTER COLUMN {column} TYPE jsonb USING {column}::jsonb, " +
                $"ALTER COLUMN {column} SET DEFAULT '{def}'::jsonb;");
        }
    }

    public override void Down()
    {
        Delete.Table("world_import_unresolved_refs");
        Delete.Table("world_entity_variants");
        Delete.Table("world_source_files");
        Delete.Table("motion_slots");
        Delete.Table("motion_models");

        Delete.Table("field_fixed_npcs");
        Delete.Table("field_boss_spawns");
        Delete.Table("field_monster_spawns");
        Delete.Table("field_spawn_points");
        Delete.Table("fields");

        Delete.Table("npc_shop_stock");
        Delete.Table("npcs");

        Delete.Table("monster_drops_plus");
        Delete.Table("monster_drop_items");
        Delete.Table("monster_drops");
        Delete.Table("monsters");

        Delete.Table("item_template_job_random");
        Delete.Table("item_template_special");
        Delete.Table("item_templates");

        Delete.Table("mailboxes");
        Delete.Table("warehouses");
        Delete.Table("characters");
        Delete.Table("accounts");
    }

    private void CreateItemTemplates()
    {
        var table = Create.Table("item_templates")
            .WithColumn("code").AsString(16).PrimaryKey()
            .WithColumn("item_code_mask").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("name").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("durability_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("durability_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("price").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_min_low").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_min_high").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_max_low").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("damage_max_high").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_rating_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_rating_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("critical_hit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("defence_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("block_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("block_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("absorb_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("speed_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("speed_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("mana_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("life_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen_min").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("stamina_regen_max").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("add_mana_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_mana_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_life_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_life_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_stamina_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("add_stamina_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_level").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_strength").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_spirit").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_talent").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_dexterity").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("req_health").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_stamina_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_stamina_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_mana_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_mana_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_life_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("restore_life_max").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("shooting_range").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("attack_speed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("job_mask").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("unique_item").AsInt64().NotNullable().WithDefaultValue(0);

        foreach (var element in Elements)
        {
            table = table
                .WithColumn($"res_{element}_min").AsInt32().NotNullable().WithDefaultValue(0)
                .WithColumn($"res_{element}_max").AsInt32().NotNullable().WithDefaultValue(0);
        }

        table = table
            .WithColumn("max_stack").AsInt32().NotNullable().WithDefaultValue(1)
            .WithColumn("potion_space").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("magic_mastery").AsFloat().NotNullable().WithDefaultValue(0);

        Create.Table("item_template_job_random")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("item_code").AsString(16).NotNullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("job_bit_code").AsInt64().NotNullable().WithDefaultValue(0);

        Create.Index("item_template_job_random_item_idx")
            .OnTable("item_template_job_random").OnColumn("item_code");

        Create.Table("item_template_special")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("item_code").AsString(16).NotNullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("bonus_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("value0").AsFloat().NotNullable().WithDefaultValue(0)
            .WithColumn("value1").AsFloat().NotNullable().WithDefaultValue(0);

        Create.Index("item_template_special_item_idx")
            .OnTable("item_template_special").OnColumn("item_code");
    }

    private void CreateMonsters()
    {
        var table = Create.Table("monsters")
            .WithColumn("name").AsString().PrimaryKey()
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
            .WithColumn("sight").AsInt32().NotNullable().WithDefaultValue(0) // Raw reach; the source squares Sight separately.
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
            .WithColumn("exp").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("active_hour").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("nature").AsInt16().NotNullable().WithDefaultValue(0x80)
            .WithColumn("brood").AsInt16().NotNullable().WithDefaultValue(0x00)
            .WithColumn("class").AsInt16().NotNullable().WithDefaultValue(0);

        foreach (var element in Elements)
        {
            table = table.WithColumn($"res_{element}").AsInt32().NotNullable().WithDefaultValue(0);
        }

        table = table
            .WithColumn("stun_pct").AsInt32().NotNullable().WithDefaultValue(100)
            .WithColumn("display_name").AsString(128).NotNullable().WithDefaultValue("Monster")
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1);

        Create.Table("monster_drops")
            .WithColumn("monster_name").AsString().PrimaryKey()
                .ForeignKey("monsters", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("max_items_per_kill").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("all_players_see").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Table("monster_drop_items")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("monster_name").AsString().NotNullable()
                .ForeignKey("monsters", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("kind").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).Nullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.SetNull)
            .WithColumn("gold_min").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("gold_max").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("monster_drop_items_monster_idx")
            .OnTable("monster_drop_items").OnColumn("monster_name");

        Create.Table("monster_drops_plus")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("monster_name").AsString().NotNullable()
                .ForeignKey("monsters", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).Nullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.SetNull);

        Create.Index("monster_drops_plus_monster_idx")
            .OnTable("monster_drops_plus").OnColumn("monster_name");
    }

    private void CreateNpcs()
    {
        Create.Table("npcs")
            .WithColumn("name").AsString().PrimaryKey()
            .WithColumn("model").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("linked_file").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("svc_warehouse").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_shop").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_skill_master").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_job_change").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_teleport").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_item_mix").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_item_aging").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_smelting").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_manufacture").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_mixture_reset").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_clan").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_bless_castle").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_quest").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_event_booth").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_collect_money").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("svc_give_money").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("job_change_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("teleport_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("quest_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("quest_param").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("dialog").AsString().NotNullable().WithDefaultValue("[]")
            .WithColumn("sound_code").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("move_range").AsInt32().NotNullable().WithDefaultValue(64)
            .WithColumn("model_scale").AsFloat().NotNullable().WithDefaultValue(1);

        Create.Table("npc_shop_stock")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("npc_name").AsString().NotNullable()
                .ForeignKey("npcs", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("tab").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ordinal").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("item_code").AsString(16).NotNullable()
                .ForeignKey("item_templates", "code").OnDelete(System.Data.Rule.Cascade);

        Create.Index("npc_shop_stock_npc_idx")
            .OnTable("npc_shop_stock").OnColumn("npc_name");
    }

    private void CreateFields()
    {
        Create.Table("fields")
            .WithColumn("field_code").AsInt32().PrimaryKey()
            .WithColumn("ase_base").AsString().NotNullable().WithDefaultValue("")
            .WithColumn("max_concurrent_monsters").AsInt32().NotNullable().WithDefaultValue(10)
            .WithColumn("max_per_spawn_point").AsInt32().NotNullable().WithDefaultValue(3)
            .WithColumn("spawn_interval").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("per_point_cooldown_ms").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Table("field_spawn_points")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("point_index").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("state").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("x").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("z").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("field_spawn_points_field_idx")
            .OnTable("field_spawn_points").OnColumn("field_code");

        Create.Table("field_monster_spawns")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("monster_name").AsString().NotNullable()
                .ForeignKey("monsters", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("weight").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("field_monster_spawns_field_idx")
            .OnTable("field_monster_spawns").OnColumn("field_code");

        Create.Table("field_boss_spawns")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("boss_monster_name").AsString().NotNullable()
                .ForeignKey("monsters", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("escort_monster_name").AsString().Nullable()
            .WithColumn("escort_count").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("spawn_hours").AsString().NotNullable().WithDefaultValue("");

        Create.Index("field_boss_spawns_field_idx")
            .OnTable("field_boss_spawns").OnColumn("field_code");

        Create.Table("field_fixed_npcs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("field_code").AsInt32().NotNullable()
                .ForeignKey("fields", "field_code").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("npc_name").AsString().NotNullable()
                .ForeignKey("npcs", "name").OnDelete(System.Data.Rule.Cascade)
            .WithColumn("x").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("z").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("angle").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("y").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("field_fixed_npcs_field_idx")
            .OnTable("field_fixed_npcs").OnColumn("field_code");
    }

    private void CreateMotionTiming()
    {
        Create.Table("motion_models")
            .WithColumn("model_key").AsString(256).PrimaryKey()
            .WithColumn("motion_table_key").AsString(256).NotNullable();

        Create.Table("motion_slots")
            .WithColumn("motion_table_key").AsString(256).NotNullable().PrimaryKey()
            .WithColumn("slot").AsInt32().NotNullable().PrimaryKey()
            .WithColumn("state").AsInt32().NotNullable()
            .WithColumn("start_frame").AsInt32().NotNullable()
            .WithColumn("end_frame").AsInt32().NotNullable()
            .WithColumn("event_frame0").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("event_frame1").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("event_frame2").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("event_frame3").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("repeat").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    private void CreateSourceFiles()
    {
        Create.Table("world_source_files")
            .WithColumn("source_path").AsString(1024).PrimaryKey()
            .WithColumn("kind").AsString(32).NotNullable()
            .WithColumn("sha256").AsString(64).NotNullable()
            .WithColumn("content_base64").AsString().NotNullable()
            .WithColumn("fields_json").AsString().NotNullable()
            .WithColumn("field_count").AsInt32().NotNullable();
    }

    private void CreateEntityVariants()
    {
        Create.Table("world_entity_variants")
            .WithColumn("source_path").AsString(1024).PrimaryKey()
            .WithColumn("kind").AsString(16).NotNullable()
            .WithColumn("identity_key").AsString(256).NotNullable()
            .WithColumn("is_catalog_entry").AsBoolean().NotNullable()
            .WithColumn("data_json").AsString().NotNullable();
    }

    private void CreateUnresolvedReferences()
    {
        Create.Table("world_import_unresolved_refs")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("kind").AsString(32).NotNullable()
            .WithColumn("owner_key").AsString(256).NotNullable()
            .WithColumn("raw_reference").AsString(256).NotNullable()
            .WithColumn("details_json").AsString().NotNullable();
    }

    private static void VerifyEarlierBaseline(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" = 12";
        var count = Convert.ToInt64(command.ExecuteScalar());
        if (count != 1)
        {
            throw new InvalidOperationException(
                "The database uses an intermediate earlier schema. Back it up and recreate it, or run the previous release through migration 12 before upgrading.");
        }
    }
}
