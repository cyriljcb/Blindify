using Blindify.Domain.Enums;

namespace Blindify.Application.Rounds;

/// <summary>Logique de planification d'une partie — remontée depuis host/app.js (retour utilisateur
/// "config.js trop gros côté client") : le client envoie une intention (nombre de séries, nombre de
/// rounds, vivier de thèmes, durées), le serveur calcule la répartition. Voir
/// docs/refactor-decisions.md section 1.</summary>
public static class SeriesPlanner
{
    private static readonly int[] PaliersBase = [10, 20, 30, 50];
    private const int PlafondPalierMax = 3000;
    private static readonly RoundMode[] ModesPossibles = [RoundMode.Qcm, RoundMode.TapeReponse, RoundMode.PremiereLettre];

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

    private static List<string> MelangerCopie(IReadOnlyList<string> liste)
    {
        var copie = liste.ToList();
        for (var i = copie.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (copie[i], copie[j]) = (copie[j], copie[i]);
        }
        // Pioche par Pop (retire depuis la fin) — inverser pour piocher dans le même ordre que le
        // mélange plutôt que par la fin en premier n'a pas d'importance (mélange déjà aléatoire).
        return copie;
    }

    /// <summary>Palier de base (série 1), plafond visé pour le palier le plus haut de la DERNIÈRE
    /// série de la partie (architecture.md section 7 : "jusqu'à 3000 pts"). La raison géométrique
    /// dépend du nombre de séries réellement choisi pour cette partie, pas une constante fixe. Une
    /// seule série -> pas de progression possible, on garde la base telle quelle.</summary>
    public static int[] PaliersPourSerie(int indexSerie, int nombreSeriesTotal)
    {
        if (nombreSeriesTotal <= 1) return (int[])PaliersBase.Clone();

        var dernierPalierBase = PaliersBase[^1];
        var raison = Math.Pow((double)PlafondPalierMax / dernierPalierBase, 1.0 / (nombreSeriesTotal - 1));
        var facteur = Math.Pow(raison, indexSerie);
        return PaliersBase.Select(v => (int)Math.Round(v * facteur)).ToArray();
    }

    /// <summary>Tiré aléatoirement par round plutôt que configuré manuellement.</summary>
    public static List<RoundMode> PickRandomRoundModes(int count) =>
        Enumerable.Range(0, count).Select(_ => ModesPossibles[Random.Shared.Next(ModesPossibles.Length)]).ToList();
}
