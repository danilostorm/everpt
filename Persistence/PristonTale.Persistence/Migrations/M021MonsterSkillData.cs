using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The key character of each motion clip, which selects the skill a monster performs with that clip.
[Migration(21)]
public sealed class M021MonsterSkillData : Migration
{
    public override void Up()
    {
        Alter.Table("motion_slots")
            .AddColumn("key_code").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("key_code").FromTable("motion_slots");
    }
}
