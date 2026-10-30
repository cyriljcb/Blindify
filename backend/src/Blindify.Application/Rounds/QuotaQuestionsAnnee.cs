using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Rounds;

/// <summary>
/// Retour utilisateur (2026-10-25) : la cible Année « tombait trop souvent » — avec le tirage
/// pondéré 40/40/20, ~2 questions Année par série de 10 rounds en moyenne, et 3 ou plus dans près
/// d'une série sur trois. Règle retenue : exactement <see cref="GameConfig.QuestionsAnneeParSerie"/>
/// question(s) Année par série, la question bonus comptant comme un créneau de la série.
///
/// Tirage séquentiel (même principe qu'un tirage sans remise) : à chaque créneau éligible, la
/// probabilité de poser l'Année vaut questions manquantes / créneaux éligibles restants (celui-ci
/// compris) — la position est donc uniforme dans la série, et le dernier créneau éligible la force
/// si elle n'est pas encore tombée. Créneau éligible = morceau à l'année connue et dont la cible
/// n'est pas forcée à Film dans le thème de la série (TagsOeuvre.ForceCibleFilm). Le morceau du bonus n'est pas connu
/// avant StartBonusRound : il est compté éligible, sauf série purement œuvre — s'il s'avère inéligible,
/// la série n'a pas de question Année plutôt que d'en forcer une absurde.
/// </summary>
public static class QuotaQuestionsAnnee
{
    public static bool EstEligible(Track track, IReadOnlyList<string> tagsSerie) =>
        track.Year is not null && !TagsOeuvre.ForceCibleFilm(track, tagsSerie);

    /// <summary>true : forcer l'Année ; false : l'exclure ; null : pas de quota (tirage pondéré
    /// historique via GameConfig.PoidsCibleAnnee).</summary>
    public static bool? Decider(int? quota, int dejaPosees, int creneauxEligiblesRestants, bool eligible, Random random)
    {
        if (quota is null) return null;
        if (!eligible) return false;

        var manquantes = quota.Value - dejaPosees;
        if (manquantes <= 0) return false;
        if (manquantes >= creneauxEligiblesRestants) return true;
        return random.NextDouble() < (double)manquantes / creneauxEligiblesRestants;
    }

    public static bool? PourRound(Series serie, int roundIndex, Func<string, Track?> resoudre, GameConfig config, Random random)
    {
        var track = resoudre(serie.Rounds[roundIndex].TrackId);
        var restants = serie.Rounds.Skip(roundIndex).Count(r => resoudre(r.TrackId) is { } t && EstEligible(t, serie.Tags))
            + (BonusPeutPorterAnnee(serie) ? 1 : 0);
        return Decider(config.QuestionsAnneeParSerie, DejaPosees(serie, roundIndex), restants,
            track is not null && EstEligible(track, serie.Tags), random);
    }

    public static bool? PourBonus(Series serie, Track track, GameConfig config, Random random) =>
        Decider(config.QuestionsAnneeParSerie, DejaPosees(serie, serie.Rounds.Count), 1, EstEligible(track, serie.Tags), random);

    private static int DejaPosees(Series serie, int avantIndex) =>
        serie.Rounds.Take(avantIndex).Count(r => r.DebutRound is not null && r.Cible == RoundCible.Annee);

    private static bool BonusPeutPorterAnnee(Series serie) =>
        !(serie.Tags.Count > 0 && serie.Tags.All(TagsOeuvre.EstTagOeuvre));
}
