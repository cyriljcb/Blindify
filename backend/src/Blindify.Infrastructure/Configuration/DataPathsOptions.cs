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

    /// <summary>V2, section 12.4 — écrit UNIQUEMENT par le backend (FlagsRepository.Ajouter), jamais par
    /// les scripts (voir CLAUDE.md).</summary>
    public required string FlagsPath { get; set; }

    /// <summary>V2, section 12.4 — écrit UNIQUEMENT par data/scripts/import_flag_resolutions.py, jamais
    /// par le backend (lecture seule, chargé une fois au démarrage comme tracks.json).</summary>
    public required string FlagsResolutionsPath { get; set; }

    public string? AudioPath { get; set; }
    public string? CoversPath { get; set; }

    /// <summary>
    /// Racine data/ (parent de audio/ et covers/) — servie en fichiers statiques sous /files pour que
    /// le host puisse lire l'audio par HTTP (les chemins de tracks.json, ex. "audio/xxx.mp3", sont déjà
    /// relatifs à cette racine).
    /// </summary>
    public required string RootPath { get; set; }
}
