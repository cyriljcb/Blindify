namespace Blindify.Infrastructure.Stats;

/// <summary>Miroir d'une entrée stats.json v2 — clé "Mode:Cible" (ex. "Qcm:Titre"), préfixe "Bonus:"
/// pour la question bonus. Voir architecture.md, section stats.json v2 (V2, socle statistiques).</summary>
public class ReponseStatDto
{
    public int N { get; set; }
    public int Correct { get; set; }
    public int Absent { get; set; }
    public long TempsCorrectCumulMs { get; set; }

    /// <summary>Non-null uniquement pour les entrées à cible Année (V2, section 12.5) — cumul des
    /// écarts (en années) des réponses correctes ET incorrectes ayant fourni un nombre valide.</summary>
    public long? EcartCumul { get; set; }
}
