using System.Globalization;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Awards;

/// <summary>Calcule les titres de fin de partie (V2, section 12.6) — pur, zéro dépendance ASP.NET,
/// appelé depuis GameHub.EndGame. Classe statique plutôt qu'un service injecté (pas d'état, pas de
/// dépendance externe autre que le résolveur de morceaux passé en paramètre) — même pattern que
/// RoundStatsAggregator pour ce type d'agrégation pure. En mode équipes, les titres restent
/// individuels (les réponses le sont, voir architecture.md section 8) : aucune logique spécifique
/// n'est nécessaire ici, Player.Score et RoundAnswer/BonusAnswer sont déjà individuels.</summary>
public static class TitresService
{
    private const int SeuilEclairMinReponses = 3;
    private const double SeuilSniperTauxParticipationRounds = 0.5;
    private const int SeuilSpecialisteMinRounds = 3;
    private const double SeuilSpecialisteTauxMin = 0.75;
    private const double SeuilSpecialisteFrequenceTagMax = 0.8;
    private const int SeuilHorlogeMinReponses = 2;
    private const int SeuilKamikazeMinBonus = 2;
    private const int SeuilPrudentMinOccurrences = 3;
    private const int SeuilRemontadaMinPlaces = 2;
    private const int SeuilPanneauMinOccurrences = 2;
    private const int MaxTitresParJoueur = 2;
    private const int PalierMaxIndex = 3; // SeriesConfig.PaliersDeMise a toujours exactement 4 paliers (0..3)

    // Ordre du catalogue (section 12.6) — départage les titres à égalité de rareté (même nombre de
    // gagnants) lors de l'attribution "du plus rare au plus courant" ; FIDELE n'y figure pas (jamais
    // dans candidatsTries, voir CalculerTitres, c'est le repli final).
    private static readonly string[] OrdreCatalogue =
        ["ECLAIR", "SNIPER", "SPECIALISTE", "HORLOGE", "ROI_BONUS", "KAMIKAZE", "PRUDENT", "REMONTADA", "PANNEAU", "CHAT_NOIR"];

    private class StatsJoueur
    {
        public int RoundsRepondus;
        public int RoundsCorrects;
        public long TempsCorrectCumulMs;
        public int CorrectsAvecTemps;

        public int AnneeRepondus;
        public long EcartAnneeCumul;

        public int PanneauOccurrences;

        public int BonusNetPoints;
        public int BonusMisesTotal;
        public int BonusMisesAuMax;
        public int PalierSafeOuAbsenceBonus;

        public int PertesMauvaisesReponses; // toujours <= 0

        public readonly Dictionary<string, (int Total, int Corrects)> ParTag = new(StringComparer.OrdinalIgnoreCase);
    }

    public static List<TitreResultat> CalculerTitres(GameSession session, Func<string, Track?> resolveTrack)
    {
        var joueurs = session.Players;
        if (joueurs.Count == 0) return [];

        var stats = joueurs.ToDictionary(p => p.PlayerId, _ => new StatsJoueur());
        var tagsFrequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalRoundsClassiques = 0;

        // Rejoue chaque round classique / question bonus dans l'ordre chronologique (ordre naturel
        // de SeriesList/Series.Rounds) pour accumuler les statistiques par joueur, ET pour permettre
        // à REMONTADA de reconstruire le classement à mi-partie (voir CalculerPlacesGagnees).
        var evenementsParUnite = new List<List<(string PlayerId, int Points)>>();

        foreach (var serie in session.SeriesList)
        {
            foreach (var round in serie.Rounds)
            {
                totalRoundsClassiques++;
                var track = resolveTrack(round.TrackId);
                if (track is not null)
                    foreach (var tag in track.Tags)
                        tagsFrequence[tag] = tagsFrequence.GetValueOrDefault(tag) + 1;

                var evenementsRound = new List<(string, int)>();
                foreach (var reponse in round.Reponses)
                {
                    evenementsRound.Add((reponse.PlayerId, reponse.Points));
                    if (!stats.TryGetValue(reponse.PlayerId, out var s) || reponse.EstAbsent) continue;

                    s.RoundsRepondus++;
                    if (reponse.EstCorrecte)
                    {
                        s.RoundsCorrects++;
                        s.TempsCorrectCumulMs += reponse.TempsReponseMs;
                        s.CorrectsAvecTemps++;
                    }
                    else
                    {
                        s.PertesMauvaisesReponses += reponse.Points;
                    }

                    if (round.Cible == RoundCible.Annee && reponse.EcartAnnee is not null)
                    {
                        s.AnneeRepondus++;
                        s.EcartAnneeCumul += reponse.EcartAnnee.Value;
                    }

                    if (reponse.OptionChoisieEstPiege || reponse.OptionChoisieEstFeinte) s.PanneauOccurrences++;

                    if (track is not null)
                        foreach (var tag in track.Tags)
                        {
                            var (total, corrects) = s.ParTag.GetValueOrDefault(tag, (0, 0));
                            s.ParTag[tag] = (total + 1, corrects + (reponse.EstCorrecte ? 1 : 0));
                        }
                }

                evenementsParUnite.Add(evenementsRound);
            }

            var bonus = serie.BonusRound;
            if (bonus is null) continue;

            var evenementsBonus = new List<(string, int)>();
            foreach (var mise in bonus.Mises)
            {
                if (!stats.TryGetValue(mise.PlayerId, out var s)) continue;
                s.BonusMisesTotal++;
                if (mise.PalierIndex == PalierMaxIndex) s.BonusMisesAuMax++;
                if (mise.PalierIndex == 0) s.PalierSafeOuAbsenceBonus++;
            }

            foreach (var reponse in bonus.Reponses)
            {
                evenementsBonus.Add((reponse.PlayerId, reponse.Points));
                if (!stats.TryGetValue(reponse.PlayerId, out var s)) continue;

                s.BonusNetPoints += reponse.Points;

                if (reponse.EstAbsent)
                {
                    s.PalierSafeOuAbsenceBonus++;
                    continue;
                }

                if (bonus.Cible == RoundCible.Annee && reponse.EcartAnnee is not null)
                {
                    s.AnneeRepondus++;
                    s.EcartAnneeCumul += reponse.EcartAnnee.Value;
                }

                if (reponse.OptionChoisieEstPiege || reponse.OptionChoisieEstFeinte) s.PanneauOccurrences++;
                if (!reponse.EstCorrecte) s.PertesMauvaisesReponses += reponse.Points;
            }

            evenementsParUnite.Add(evenementsBonus);
        }

        var candidats = new List<TitreResultat>();

        AjouterEclair(candidats, stats);
        AjouterSniper(candidats, stats, totalRoundsClassiques);
        AjouterSpecialiste(candidats, stats, tagsFrequence, totalRoundsClassiques);
        AjouterHorloge(candidats, stats);
        AjouterRoiBonus(candidats, stats);
        AjouterKamikaze(candidats, stats);
        AjouterPrudent(candidats, stats);
        AjouterRemontada(candidats, joueurs, evenementsParUnite);
        AjouterPanneau(candidats, stats);
        AjouterChatNoir(candidats, stats);

        return Attribuer(candidats, joueurs);
    }

    /// <summary>Attribution : du titre le plus rare (le moins de gagnants) au plus courant, plafond
    /// 2 titres par joueur — un joueur déjà à son plafond est simplement retiré du groupe de gagnants
    /// d'un titre partagé (les autres gagnants du même titre le reçoivent quand même), jamais annulé
    /// pour tout le groupe. Tout joueur encore sans titre à la fin reçoit FIDELE.</summary>
    private static List<TitreResultat> Attribuer(List<TitreResultat> candidats, List<Player> joueurs)
    {
        var titresParJoueur = joueurs.ToDictionary(p => p.PlayerId, _ => 0);
        var resultat = new List<TitreResultat>();

        var candidatsTries = candidats
            .OrderBy(c => c.PlayerIds.Count)
            .ThenBy(c => Array.IndexOf(OrdreCatalogue, c.Code))
            .ToList();

        foreach (var candidat in candidatsTries)
        {
            var eligibles = candidat.PlayerIds.Where(id => titresParJoueur[id] < MaxTitresParJoueur).ToList();
            if (eligibles.Count == 0) continue;

            resultat.Add(new TitreResultat { Code = candidat.Code, Libelle = candidat.Libelle, Description = candidat.Description, PlayerIds = eligibles });
            foreach (var id in eligibles) titresParJoueur[id]++;
        }

        var sansTitre = joueurs.Where(p => titresParJoueur[p.PlayerId] == 0).Select(p => p.PlayerId).ToList();
        if (sansTitre.Count > 0)
            resultat.Add(new TitreResultat { Code = "FIDELE", Libelle = "Présent jusqu'au bout", Description = "A participé à toute la partie", PlayerIds = sansTitre });

        return resultat;
    }

    private static string FormatDecimal(double valeur, string format = "0.0") => valeur.ToString(format, CultureInfo.InvariantCulture).Replace('.', ',');

    private static void AjouterEclair(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.CorrectsAvecTemps >= SeuilEclairMinReponses).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Min(kv => (double)kv.Value.TempsCorrectCumulMs / kv.Value.CorrectsAvecTemps);
        var gagnants = eligibles.Where(kv => (double)kv.Value.TempsCorrectCumulMs / kv.Value.CorrectsAvecTemps == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "ECLAIR", Libelle = "Éclair", Description = $"{FormatDecimal(meilleur / 1000)} s en moyenne", PlayerIds = gagnants });
    }

    private static void AjouterSniper(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats, int totalRoundsClassiques)
    {
        if (totalRoundsClassiques == 0) return;
        var eligibles = stats.Where(kv => kv.Value.RoundsRepondus >= totalRoundsClassiques * SeuilSniperTauxParticipationRounds && kv.Value.RoundsRepondus > 0).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(kv => (double)kv.Value.RoundsCorrects / kv.Value.RoundsRepondus);
        var gagnants = eligibles.Where(kv => (double)kv.Value.RoundsCorrects / kv.Value.RoundsRepondus == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "SNIPER", Libelle = "Sniper", Description = $"{FormatDecimal(meilleur * 100, "0")} % de bonnes réponses", PlayerIds = gagnants });
    }

    private static void AjouterSpecialiste(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats, Dictionary<string, int> tagsFrequence, int totalRoundsClassiques)
    {
        if (totalRoundsClassiques == 0) return;

        var eligibles = new List<(string PlayerId, string Tag, double Taux)>();
        foreach (var (playerId, s) in stats)
        {
            foreach (var (tag, (total, corrects)) in s.ParTag)
            {
                if (total < SeuilSpecialisteMinRounds) continue;
                if ((double)tagsFrequence.GetValueOrDefault(tag) / totalRoundsClassiques >= SeuilSpecialisteFrequenceTagMax) continue;
                var taux = (double)corrects / total;
                if (taux < SeuilSpecialisteTauxMin) continue;
                eligibles.Add((playerId, tag, taux));
            }
        }

        if (eligibles.Count == 0) return;

        var meilleurTaux = eligibles.Max(c => c.Taux);
        // Cas limite très improbable : plusieurs tags DIFFÉRENTS à égalité exacte sur le taux max —
        // on retient le premier par ordre alphabétique plutôt que de générer plusieurs variantes
        // simultanées de "Spécialiste", pour garder un seul titre partagé cohérent.
        var tagRetenu = eligibles.Where(c => c.Taux == meilleurTaux).Select(c => c.Tag).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).First();
        var gagnants = eligibles.Where(c => c.Taux == meilleurTaux && string.Equals(c.Tag, tagRetenu, StringComparison.OrdinalIgnoreCase)).Select(c => c.PlayerId).Distinct().ToList();

        var libelleTag = char.ToUpperInvariant(tagRetenu[0]) + tagRetenu[1..].Replace('-', ' ');
        candidats.Add(new TitreResultat { Code = "SPECIALISTE", Libelle = $"Spécialiste {libelleTag}", Description = $"{FormatDecimal(meilleurTaux * 100, "0")} % de bonnes réponses sur ce thème", PlayerIds = gagnants });
    }

    private static void AjouterHorloge(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.AnneeRepondus >= SeuilHorlogeMinReponses).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Min(kv => (double)kv.Value.EcartAnneeCumul / kv.Value.AnneeRepondus);
        var gagnants = eligibles.Where(kv => (double)kv.Value.EcartAnneeCumul / kv.Value.AnneeRepondus == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "HORLOGE", Libelle = "Horloge suisse", Description = $"{FormatDecimal(meilleur)} an(s) d'écart en moyenne", PlayerIds = gagnants });
    }

    private static void AjouterRoiBonus(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.BonusNetPoints > 0).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(kv => kv.Value.BonusNetPoints);
        var gagnants = eligibles.Where(kv => kv.Value.BonusNetPoints == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "ROI_BONUS", Libelle = "Roi du bonus", Description = $"+{meilleur} points nets en question bonus", PlayerIds = gagnants });
    }

    private static void AjouterKamikaze(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.BonusMisesTotal >= SeuilKamikazeMinBonus).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(kv => (double)kv.Value.BonusMisesAuMax / kv.Value.BonusMisesTotal);
        if (meilleur <= 0) return;
        var gagnants = eligibles.Where(kv => (double)kv.Value.BonusMisesAuMax / kv.Value.BonusMisesTotal == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "KAMIKAZE", Libelle = "Kamikaze", Description = $"{FormatDecimal(meilleur * 100, "0")} % des mises au palier maximum", PlayerIds = gagnants });
    }

    private static void AjouterPrudent(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.PalierSafeOuAbsenceBonus >= SeuilPrudentMinOccurrences).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(kv => kv.Value.PalierSafeOuAbsenceBonus);
        var gagnants = eligibles.Where(kv => kv.Value.PalierSafeOuAbsenceBonus == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "PRUDENT", Libelle = "Tortue prudente", Description = $"{meilleur} mise(s) safe ou abstention(s)", PlayerIds = gagnants });
    }

    private static void AjouterPanneau(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.PanneauOccurrences >= SeuilPanneauMinOccurrences).ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(kv => kv.Value.PanneauOccurrences);
        var gagnants = eligibles.Where(kv => kv.Value.PanneauOccurrences == meilleur).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "PANNEAU", Libelle = "Tombé dans le panneau", Description = $"{meilleur} fois piégé(e) par un distracteur", PlayerIds = gagnants });
    }

    private static void AjouterChatNoir(List<TitreResultat> candidats, Dictionary<string, StatsJoueur> stats)
    {
        var eligibles = stats.Where(kv => kv.Value.PertesMauvaisesReponses < 0).ToList();
        if (eligibles.Count == 0) return;

        var pire = eligibles.Min(kv => kv.Value.PertesMauvaisesReponses);
        var gagnants = eligibles.Where(kv => kv.Value.PertesMauvaisesReponses == pire).Select(kv => kv.Key).ToList();
        candidats.Add(new TitreResultat { Code = "CHAT_NOIR", Libelle = "Chat noir", Description = $"{pire} points perdus en mauvaises réponses", PlayerIds = gagnants });
    }

    /// <summary>Rejoue les scores individuels dans l'ordre chronologique jusqu'à la moitié des unités
    /// de jeu (rounds classiques + questions bonus, arrondi au-dessus) pour comparer le classement à
    /// mi-partie au classement final — "places gagnées" = amélioration du rang (rang 1 = premier).</summary>
    private static void AjouterRemontada(List<TitreResultat> candidats, List<Player> joueurs, List<List<(string PlayerId, int Points)>> evenementsParUnite)
    {
        if (evenementsParUnite.Count == 0) return;

        var uniteMiPartie = (int)Math.Ceiling(evenementsParUnite.Count / 2.0);
        var scoreMiPartie = joueurs.ToDictionary(p => p.PlayerId, _ => 0);

        for (var i = 0; i < uniteMiPartie; i++)
            foreach (var (playerId, points) in evenementsParUnite[i])
                if (scoreMiPartie.ContainsKey(playerId))
                    scoreMiPartie[playerId] += points;

        var rangMiPartie = CalculerRangs(joueurs.Select(p => (p.PlayerId, scoreMiPartie[p.PlayerId])));
        var rangFinal = CalculerRangs(joueurs.Select(p => (p.PlayerId, p.Score)));

        var eligibles = joueurs
            .Select(p => (p.PlayerId, PlacesGagnees: rangMiPartie[p.PlayerId] - rangFinal[p.PlayerId]))
            .Where(p => p.PlacesGagnees >= SeuilRemontadaMinPlaces)
            .ToList();
        if (eligibles.Count == 0) return;

        var meilleur = eligibles.Max(p => p.PlacesGagnees);
        var gagnants = eligibles.Where(p => p.PlacesGagnees == meilleur).Select(p => p.PlayerId).ToList();
        candidats.Add(new TitreResultat { Code = "REMONTADA", Libelle = "Remontada", Description = $"+{meilleur} places entre la mi-partie et la fin", PlayerIds = gagnants });
    }

    /// <summary>Classement par compétition (rang 1 = meilleur score, égalité = même rang, le rang
    /// suivant saute d'autant — ex. 1, 1, 3, 4).</summary>
    private static Dictionary<string, int> CalculerRangs(IEnumerable<(string PlayerId, int Score)> scores)
    {
        var tries = scores.OrderByDescending(s => s.Score).ToList();
        var rangs = new Dictionary<string, int>();
        var rang = 0;
        var rangCourant = 0;
        int? dernierScore = null;

        foreach (var (playerId, score) in tries)
        {
            rang++;
            if (dernierScore is null || score != dernierScore) rangCourant = rang;
            rangs[playerId] = rangCourant;
            dernierScore = score;
        }

        return rangs;
    }
}
