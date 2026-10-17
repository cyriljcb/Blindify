using Blindify.Domain.Enums;

namespace Blindify.Application.Rounds;

/// <summary>Logique de planification d'une partie — remontée depuis host/app.js (retour utilisateur
/// "config.js trop gros côté client") : le client envoie une intention (nombre de séries, nombre de
/// rounds, vivier de thèmes, durées), le serveur calcule la répartition. Voir
/// docs/refactor-decisions.md section 1.</summary>
public static class SeriesPlanner
{
    private static readonly int[] PaliersBase = [10, 20, 30, 50];
    private static readonly RoundMode[] ModesPossibles = [RoundMode.Qcm, RoundMode.TapeReponse, RoundMode.PremiereLettre];

    /// <summary>Vote des thèmes (retour utilisateur 2026-10-17) : choisit nombreSeries thèmes distincts
    /// parmi ceux qui ont reçu au moins une voix. Autant de thèmes votés que de séries → tous joués.
    /// Moins de séries → tirage, en couvrant d'abord le plus de votants possible (au moins un thème
    /// par personne quand c'est faisable) : on prend tant qu'il reste des votants non couverts le thème
    /// qui en couvre le plus (égalités départagées au hasard), puis on complète au hasard parmi les
    /// thèmes votés restants. Jamais plus de séries que de thèmes votés (ArgumentException). Ordre
    /// renvoyé mélangé — c'est l'ordre de jeu des séries.</summary>
    public static List<string> ChoisirThemesDepuisVotes(IReadOnlyDictionary<string, List<string>> votesParJoueur, int nombreSeries, Random? random = null)
    {
        random ??= Random.Shared;
        var votes = votesParJoueur.Values.Where(v => v.Count > 0).Select(v => v.Distinct().ToList()).ToList();
        var themesVotes = votes.SelectMany(v => v).Distinct().ToList();

        if (nombreSeries > themesVotes.Count)
            throw new ArgumentException($"Seulement {themesVotes.Count} thème(s) voté(s) pour {nombreSeries} série(s) demandée(s).");

        var choisis = new List<string>();
        var nonCouverts = votes.ToList();

        while (choisis.Count < nombreSeries && nonCouverts.Count > 0)
        {
            var meilleur = nonCouverts.SelectMany(v => v).Distinct()
                .Select(theme => (theme, couverts: nonCouverts.Count(v => v.Contains(theme)), alea: random.Next()))
                .OrderByDescending(c => c.couverts).ThenBy(c => c.alea)
                .First().theme;
            choisis.Add(meilleur);
            nonCouverts.RemoveAll(v => v.Contains(meilleur));
        }

        var restants = MelangerCopie(themesVotes.Except(choisis).ToList(), random);
        choisis.AddRange(restants.Take(nombreSeries - choisis.Count));

        return MelangerCopie(choisis, random);
    }

    /// <summary>Une série = un thème, jamais un mélange. Chaque série tirée reçoit un thème distinct
    /// pioché au hasard dans le vivier, sans répétition tant qu'il reste des thèmes non utilisés —
    /// au-delà (plus de séries que de thèmes fournis), le vivier est remélangé et repioché en boucle.
    /// themes vide (thème "aléatoire") -> toutes les séries piochent dans tout le catalogue ([]).</summary>
    public static List<List<string>> AssignerThemesAuxSeries(IReadOnlyList<string> themes, int nombreSeries)
    {
        if (themes.Count == 0)
            return Enumerable.Range(0, nombreSeries).Select(_ => new List<string>()).ToList();

        var assignation = new List<List<string>>(nombreSeries);
        var pioche = new List<string>();

        for (var i = 0; i < nombreSeries; i++)
        {
            if (pioche.Count == 0) pioche = MelangerCopie(themes);
            var theme = pioche[^1];
            pioche.RemoveAt(pioche.Count - 1);
            assignation.Add([theme]);
        }

        return assignation;
    }

    private static List<string> MelangerCopie(IReadOnlyList<string> liste, Random? random = null)
    {
        random ??= Random.Shared;
        var copie = liste.ToList();
        for (var i = copie.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (copie[i], copie[j]) = (copie[j], copie[i]);
        }
        // Pioche par Pop (retire depuis la fin) — inverser pour piocher dans le même ordre que le
        // mélange plutôt que par la fin en premier n'a pas d'importance (mélange déjà aléatoire).
        return copie;
    }

    /// <summary>Palier de base (série 1) multiplié par un ratio géométrique CONSTANT
    /// (GameConfig.FacteurProgressionPaliers) élevé à la puissance de l'index de série — voir ce champ
    /// pour la raison du changement (V2) par rapport à l'ancien calcul "viser 3000 pts à la dernière
    /// série", qui dépendait du nombre total de séries et produisait une progression trop brusque avec
    /// peu de séries.</summary>
    public static int[] PaliersPourSerie(int indexSerie, double facteurProgression)
    {
        var facteur = Math.Pow(facteurProgression, indexSerie);
        return PaliersBase.Select(v => (int)Math.Round(v * facteur)).ToArray();
    }

    /// <summary>Tiré aléatoirement par round plutôt que configuré manuellement.</summary>
    public static List<RoundMode> PickRandomRoundModes(int count) =>
        Enumerable.Range(0, count).Select(_ => ModesPossibles[Random.Shared.Next(ModesPossibles.Length)]).ToList();
}
