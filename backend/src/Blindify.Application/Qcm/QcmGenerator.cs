using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Application.Qcm;

public class QcmGenerator : IQcmGenerator
{
    private const int NombreDistracteurs = 3;

    public QcmOptions GenererOptions(Track correct, IReadOnlyList<Track> pool, GameConfig config, Random random)
    {
        var distracteurs = new List<string>();

        if (correct.TrapWith.Count > 0 && random.NextDouble() < config.ProbabiliteQcmPiege)
        {
            var pieges = correct.TrapWith
                .Where(id => pool.Any(t => t.Id == id))
                .ToList();

            if (pieges.Count > 0)
                distracteurs.Add(pieges[random.Next(pieges.Count)]);
        }

        var dejaChoisis = new HashSet<string>(distracteurs) { correct.Id };

        var poolGenreTag = pool
            .Where(t => !dejaChoisis.Contains(t.Id) && PartageGenreOuTag(t, correct))
            .Select(t => t.Id)
            .ToList();

        distracteurs.AddRange(TirerSansRemise(poolGenreTag, NombreDistracteurs - distracteurs.Count, dejaChoisis, random));

        if (distracteurs.Count < NombreDistracteurs)
        {
            var poolGlobal = pool
                .Where(t => !dejaChoisis.Contains(t.Id))
                .Select(t => t.Id)
                .ToList();

            distracteurs.AddRange(TirerSansRemise(poolGlobal, NombreDistracteurs - distracteurs.Count, dejaChoisis, random));
        }

        var options = new List<string>(distracteurs) { correct.Id };
        Melanger(options, random);

        return new QcmOptions(correct.Id, options);
    }

    private static bool PartageGenreOuTag(Track a, Track b) =>
        a.Genres.Intersect(b.Genres).Any() || a.Tags.Intersect(b.Tags).Any();

    private static List<string> TirerSansRemise(List<string> candidats, int nombre, HashSet<string> dejaChoisis, Random random)
    {
        var resultat = new List<string>();
        var disponibles = candidats.Where(id => !dejaChoisis.Contains(id)).ToList();

        while (resultat.Count < nombre && disponibles.Count > 0)
        {
            var index = random.Next(disponibles.Count);
            var id = disponibles[index];
            resultat.Add(id);
            dejaChoisis.Add(id);
            disponibles.RemoveAt(index);
        }

        return resultat;
    }

    private static void Melanger<T>(List<T> liste, Random random)
    {
        for (var i = liste.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (liste[i], liste[j]) = (liste[j], liste[i]);
        }
    }
}
