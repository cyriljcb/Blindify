namespace Blindify.Domain.Configuration;

/// <summary>
/// Paramètres globaux à la partie (jamais de constantes en dur) — voir architecture.md section 11.
/// </summary>
public class GameConfig
{
    public double ProbabiliteQcmPiege { get; set; } = 0.05;

    /// <summary>Retour utilisateur : piège purement visuel, distinct de ProbabiliteQcmPiege (qui
    /// pioche un VRAI morceau souvent confondu, trapWith). Ici, un des distracteurs affiche le
    /// champ opposé du morceau correct (ex. cible Titre -> une option affiche l'auteur du morceau
    /// correct comme s'il s'agissait d'un titre). L'ID du distracteur ne change pas, donc le
    /// cliquer reste compté comme une mauvaise réponse normalement.</summary>
    public double ProbabiliteQcmFeinteChamp { get; set; } = 0.10;

    /// <summary>Feinte texte inventé (retour utilisateur, ex. Bastille - Pompéi -> "Baptiste") :
    /// distincte de ProbabiliteQcmPiege (vrai morceau, trapWith) et de ProbabiliteQcmFeinteChamp
    /// (champ opposé du morceau correct). Ici le texte vient de Track.TrapTexteArtiste, un
    /// leurre écrit à la main, sans rapport avec un morceau réel du catalogue. Volontairement
    /// basse : un leurre inventé trop fréquent devient injuste plutôt qu'amusant.</summary>
    public double ProbabiliteQcmFeinteTexteArtiste { get; set; } = 0.05;

    /// <summary>Ratio utilisé dans seuil = max(1, floor(longueur(texteNormalisé) × ratio)) — appliqué
    /// seulement au-delà de LongueurMinimalePourTolerance.</summary>
    public double SeuilToleranceLevenshteinRatio { get; set; } = 0.2;

    /// <summary>À partir de cette longueur (texte normalisé), le seuil ratio ci-dessus s'applique.
    /// Retour utilisateur : sur un texte très court, le seuil minimal d'1 caractère (voir
    /// SeuilToleranceLevenshteinRatio) rendait presque n'importe quelle réponse acceptable (ex.
    /// "Xo" accepté pour "Go") — d'où la zone intermédiaire ci-dessous entre les deux seuils de
    /// longueur.</summary>
    public int LongueurMinimalePourTolerance { get; set; } = 10;

    /// <summary>En dessous de cette longueur (texte normalisé), aucune tolérance : réponse exacte
    /// exigée (protège les réponses à 2-3 lettres du bug "Xo"/"Go" ci-dessus). Entre cette longueur
    /// et LongueurMinimalePourTolerance, la tolérance fixe ToleranceFixeReponseCourte s'applique
    /// (au lieu du ratio, qui donnerait un seuil ridiculement bas sur un texte aussi court).</summary>
    public int LongueurMinimalePourToleranceFixe { get; set; } = 4;

    /// <summary>Nombre de caractères d'écart toléré entre LongueurMinimalePourToleranceFixe et
    /// LongueurMinimalePourTolerance. Retour utilisateur : "Hayley" tapé pour "Halsey" (6 caractères,
    /// 2 caractères d'écart) comptait faux car la réponse exacte était exigée sous 10 caractères —
    /// beaucoup trop strict pour une simple faute de frappe sur un nom de cette longueur.</summary>
    public int ToleranceFixeReponseCourte { get; set; } = 2;

    /// <summary>Probabilité qu'une question bonus tirée en mode Qcm devienne une "course" (retour
    /// utilisateur : le premier qui répond, juste ou faux, décide seul du sort de sa mise ; les
    /// autres ne gagnent ni ne perdent rien tant qu'un joueur a répondu). Ne s'applique jamais aux
    /// modes TapeReponse/PremiereLettre — voir BonusRoundService.CreerBonusRound.</summary>
    public double ProbabiliteBonusCourse { get; set; } = 0.5;

    public bool RalentissementBonusActive { get; set; } = true;

    /// <summary>Retour utilisateur : 0.8 pas assez perceptible, encore ralenti.</summary>
    public double FacteurRalentissementBonus { get; set; } = 0.65;
}
