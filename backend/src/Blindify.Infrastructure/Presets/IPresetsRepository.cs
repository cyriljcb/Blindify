namespace Blindify.Infrastructure.Presets;

/// <summary>Configurations de partie enregistrées (data/presets.json) — écrit par le backend, comme
/// stats.json et flags.json, jamais tracks.json (voir CLAUDE.md).</summary>
public interface IPresetsRepository
{
    IReadOnlyList<PresetEntryDto> Lister();

    /// <summary>Remplace une configuration existante du même nom (sans tenir compte de la casse).</summary>
    void Enregistrer(PresetEntryDto preset);

    /// <summary>Retourne false si aucune configuration ne porte ce nom.</summary>
    bool Supprimer(string nom);
}
