namespace Blindify.Infrastructure.Configuration;

/// <summary>
/// Chemins des données montées en volume (jamais copiées dans l'image Docker) — voir architecture.md section 2.
/// Bindé depuis la section de configuration "Data" (ex. Data__TracksPath en variable d'environnement).
/// </summary>
public class DataPathsOptions
{
    public const string SectionName = "Data";

    public required string TracksPath { get; set; }
    public required string StatsPath { get; set; }
    public string? AudioPath { get; set; }
    public string? CoversPath { get; set; }
}
