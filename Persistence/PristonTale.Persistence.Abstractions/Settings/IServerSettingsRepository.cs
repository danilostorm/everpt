namespace PristonTale.Persistence.Abstractions.Settings;

public sealed record ServerSettingSeed(string Name, string DefaultValue, string Description);

public interface IServerSettingsRepository
{
    // Adds the rows that are missing with their defaults and refreshes every known row's description; values stay.
    Task SeedAsync(IReadOnlyList<ServerSettingSeed> seeds, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, string>> LoadAsync(CancellationToken ct = default);

    // False when no row has this name.
    Task<bool> SetAsync(string name, string value, CancellationToken ct = default);
}
