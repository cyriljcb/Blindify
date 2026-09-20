using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Domain.Statistics;

namespace Blindify.Application.Stats;

/// <summary>Agrège un round classique ou une question bonus déjà terminés en un delta prêt pour
/// StatsRepository.EnregistrerResultatsRound (V2, socle statistiques de réponse) — jamais appelé
/// avant que Round.Reponses/BonusRound.Reponses ne soient complets (TerminerParTimeout déjà exécuté),
/// voir RoundTimerCoordinator/BonusTimerCoordinator. Logique dupliquée entre round classique et bonus
/// plutôt que partagée via une interface commune — même dette assumée que RoundService/
/// BonusRoundService.EstPremiereLettreCorrecte, voir architecture.md section 11.</summary>
public static class RoundStatsAggregator
{
    public static RoundStatsUpdate PourRoundClassique(Round round)
    {
        var cle = $"{round.Mode}:{round.Cible}";
        var repondants = round.Reponses.Where(r => !r.EstAbsent).ToList();

        long? ecartCumul = round.Cible == RoundCible.Annee
            ? repondants.Where(r => r.EcartAnnee is not null).Sum(r => (long)r.EcartAnnee!.Value)
            : null;

        return new RoundStatsUpdate
        {
            TrackId = round.TrackId,
            CleModeCible = cle,
            N = round.Reponses.Count,
            Correct = round.Reponses.Count(r => r.EstCorrecte),
            Absent = round.Reponses.Count(r => r.EstAbsent),
            TempsCorrectCumulMs = repondants.Where(r => r.EstCorrecte).Sum(r => (long)r.TempsReponseMs),
            EcartCumul = ecartCumul,
            Confusions = ConfusionsDepuisOptions(round.Options, round.TrackId, repondants.Count, repondants.Select(r => r.OptionChoisieTrackId)),
        };
    }

    public static RoundStatsUpdate PourBonus(BonusRound bonusRound)
    {
        var cle = $"Bonus:{bonusRound.Mode}:{bonusRound.Cible}";
        var repondants = bonusRound.Reponses.Where(r => !r.EstAbsent).ToList();

        long? ecartCumul = bonusRound.Cible == RoundCible.Annee
            ? repondants.Where(r => r.EcartAnnee is not null).Sum(r => (long)r.EcartAnnee!.Value)
            : null;

        return new RoundStatsUpdate
        {
            TrackId = bonusRound.TrackId,
            CleModeCible = cle,
            N = bonusRound.Reponses.Count,
            Correct = bonusRound.Reponses.Count(r => r.EstCorrecte),
            Absent = bonusRound.Reponses.Count(r => r.EstAbsent),
            TempsCorrectCumulMs = repondants.Where(r => r.EstCorrecte).Sum(r => (long)r.TempsReponseMs),
            EcartCumul = ecartCumul,
            Confusions = ConfusionsDepuisOptions(bonusRound.Options, bonusRound.TrackId, repondants.Count, repondants.Select(r => r.OptionChoisieTrackId)),
        };
    }

    /// <summary>Presente = nombre de répondants (l'option était affichée à tout le monde, pas
    /// seulement à qui l'a choisie) ; Choisi = nombre l'ayant effectivement sélectionnée. Exclut la
    /// bonne réponse (measure des CONFUSIONS entre morceaux, pas la justesse déjà comptée dans
    /// Correct) et les options dont le texte a été modifié par une feinte (mesurerait une confusion
    /// de texte, pas de morceau) — voir RoundOption.EstFeinte.</summary>
    private static List<ConfusionDelta> ConfusionsDepuisOptions(
        List<RoundOption>? options, string correctTrackId, int nombreRepondants, IEnumerable<string?> choixDesRepondants)
    {
        if (options is null) return [];

        var comptageChoix = choixDesRepondants
            .Where(c => c is not null)
            .GroupBy(c => c!)
            .ToDictionary(g => g.Key, g => g.Count());

        return options
            .Where(o => o.TrackId != correctTrackId && !o.EstFeinte)
            .Select(o => new ConfusionDelta
            {
                TrackId = o.TrackId,
                Presente = nombreRepondants,
                Choisi = comptageChoix.GetValueOrDefault(o.TrackId),
            })
            .ToList();
    }
}
