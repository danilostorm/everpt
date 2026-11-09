using FluentMigrator;

namespace PristonTale.Persistence.Migrations;

// The starter gifts gained the off-hand as their third: a count past the armour moves up one, so a served character
// stays served and one midway keeps the crystals it is owed (it skips the off-hand).
[Migration(34)]
public sealed class M034StarterOffHand : Migration
{
    public override void Up() =>
        Execute.Sql("UPDATE characters SET starter_gifts_received = starter_gifts_received + 1 WHERE starter_gifts_received >= 3");

    public override void Down() =>
        Execute.Sql("UPDATE characters SET starter_gifts_received = starter_gifts_received - 1 WHERE starter_gifts_received >= 4");
}
