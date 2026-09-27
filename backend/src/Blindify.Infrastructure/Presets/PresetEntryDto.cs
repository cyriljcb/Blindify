namespace Blindify.Infrastructure.Presets;

/// <summary>Une configuration de partie enregistrée sous un nom (refonte UI, lot 3) — une entrée de
/// data/presets.json. Reprend les réglages du panneau host : ceux transmis à ConfigurerPartie
/// (séries, rounds, durée, thèmes, écart) et ceux purement locaux à la page host (musique continue,
/// délai d'enchaînement), pour qu'un clic restaure tout le formulaire.</summary>
public class PresetEntryDto
{
    public required string Nom { get; set; }
    public int NombreSeries { get; set; }
    public int NombreRoundsClassiques { get; set; }
    public int DureeFenetreReponseMs { get; set; }
    public List<string> ThemesVivier { get; set; } = [];
    public bool MusiqueContinue { get; set; } = true;
    public int DelaiEnchainementMs { get; set; } = 10000;
    public bool AfficherEcart { get; set; } = true;
}
