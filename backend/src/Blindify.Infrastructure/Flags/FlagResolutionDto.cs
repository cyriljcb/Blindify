namespace Blindify.Infrastructure.Flags;

/// <summary>Une entrée de data/flags_resolutions.json (V2, section 12.4) — dictionnaire clé = FlagEntryDto.Id,
/// écrit UNIQUEMENT par data/scripts/import_flag_resolutions.py, jamais par le backend (lecture seule,
/// chargé une fois au démarrage comme tracks.json). Un signalement est "ouvert" tant que son id n'apparaît
/// pas ici.</summary>
public class FlagResolutionDto
{
    /// <summary>"corrige" / "ignore" / "retire" — voir data/scripts/import_flag_resolutions.py.</summary>
    public required string Resolution { get; set; }

    public required string Date { get; set; }
}
