namespace Blindify.Application.Qcm;

/// <summary>Génère les options QCM pour la cible Année (V2, section 12.5) — distinct de IQcmGenerator
/// qui tire des morceaux : ici les options sont de simples années, sans TrackId/feinte/piège.</summary>
public interface IAnneeQcmGenerator
{
    /// <summary>4 années triées, dont anneeCorrecte, position de la bonne réponse tirée uniformément,
    /// écart d'au moins 2 ans entre options, jamais plus de 10 ans de anneeCorrecte, jamais dans le
    /// futur (année courante en UTC).</summary>
    List<int> GenererOptions(int anneeCorrecte, Random random);
}
