namespace Blindify.Infrastructure.Stats;

/// <summary>Miroir d'une entrée stats.json — voir architecture.md section 4. Reponses/Confusions
/// ajoutés en V2 (section 12.1) : une entrée v1 (PlayCount seul) reste lisible telle quelle, ces deux
/// dictionnaires se désérialisent alors vides (System.Text.Json ne touche pas aux propriétés absentes
/// du JSON, qui gardent leur valeur d'initialisation) — jamais d'exception sur les données existantes.</summary>
public class StatsEntryDto
{
    public int PlayCount { get; set; }
    public Dictionary<string, ReponseStatDto> Reponses { get; set; } = [];
    public Dictionary<string, ConfusionStatDto> Confusions { get; set; } = [];
}
