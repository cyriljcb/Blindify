using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Application.Qcm;

public class QcmGenerator : IQcmGenerator
{
    private const int NombreDistracteurs = 3;

    public QcmOptions GenererOptions(Track correct, IReadOnlyList<Track> pool, GameConfig config, Random random)
    {
        var distracteurs = new List<Track>();
        var idsChoisis = new HashSet<string> { correct.Id };
        var artistesChoisis = new HashSet<string> { correct.Artist };

        if (correct.TrapWith.Count > 0 && random.NextDouble() < config.ProbabiliteQcmPiege)
        {
            // Filtré par idsChoisis/artistesChoisis comme tout le reste du pipeline : un piège
            // curé qui partage l'auteur de la bonne réponse afficherait deux fois le même texte
            // en cible Auteur (retour utilisateur : "Calvin Harris" présent deux fois, dont une
            // fois comme bonne réponse). Skip silencieux si plus aucun piège n'est éligible —
            // jamais forcer un doublon pour préserver le piège.
            var pieges = correct.TrapWith
                .Select(id => pool.FirstOrDefault(t => t.Id == id))
                .Where(t => t is not null)
                .Cast<Track>()
                .Where(t => !idsChoisis.Contains(t.Id) && !artistesChoisis.Contains(t.Artist))
                .ToList();

            if (pieges.Count > 0)
                Choisir(distracteurs, pieges, idsChoisis, artistesChoisis, random);
        }

        // Évite si possible un distracteur du même auteur que la bonne réponse OU qu'un autre
        // distracteur déjà choisi (retour utilisateur : deux mauvaises réponses avec le même
        // artiste, illisible en cible Auteur puisqu'elles afficheraient alors un texte identique).
        // Best-effort seulement — voir filet de sécurité plus bas, jamais au prix de bloquer le
        // round faute d'options.
        var frequenceGenres = ConstruireFrequenceGenres(pool);
        CompleterDepuisPool(distracteurs, pool, NombreDistracteurs, idsChoisis, artistesChoisis, random,
            t => PartageGenreOuTag(t, correct, frequenceGenres, pool.Count));

        // Repli intermédiaire : pas assez de morceaux du même genre/tag (catalogue trop niche, ou
        // morceau sans genre renseigné — ~40 % du catalogue actuel) -> on privilégie les morceaux de
        // l'année la plus proche plutôt que de sauter directement à un tirage totalement aléatoire.
        // Un tri par proximité (plutôt qu'un bucket "même décennie") évite l'effet de bord où deux
        // morceaux à un an d'écart (1999/2001) tombent dans des décennies différentes.
        if (distracteurs.Count < NombreDistracteurs && correct.Year is not null)
            CompleterParAnneeProche(distracteurs, pool, NombreDistracteurs, idsChoisis, artistesChoisis, random, correct.Year.Value);

        if (distracteurs.Count < NombreDistracteurs)
            CompleterDepuisPool(distracteurs, pool, NombreDistracteurs, idsChoisis, artistesChoisis, random, _ => true);

        // Filet de sécurité : catalogue trop restreint pour exclure les auteurs déjà choisis -> on
        // retombe sur les IDs déjà utilisés uniquement, plutôt que de livrer un round avec moins de
        // 4 options.
        if (distracteurs.Count < NombreDistracteurs)
        {
            var disponibles = pool.Where(t => !idsChoisis.Contains(t.Id)).ToList();
            while (distracteurs.Count < NombreDistracteurs && disponibles.Count > 0)
            {
                var index = random.Next(disponibles.Count);
                var track = disponibles[index];
                distracteurs.Add(track);
                idsChoisis.Add(track.Id);
                disponibles.RemoveAt(index);
            }
        }

        var options = distracteurs.Select(t => t.Id).Append(correct.Id).ToList();
        Melanger(options, random);

        return new QcmOptions(correct.Id, options);
    }

    // Un genre "trop générique" (couvrant une trop grosse part du catalogue, ex. "variété
    // française"/"chanson"/"french pop" à ~9-10 % chacun sur le catalogue actuel) ne suffit pas
    // à lui seul à rendre deux morceaux cohérents comme distracteur — retour utilisateur : un
    // featuring Dua Lipa/Angèle tagué "chanson"/"variété française" (héritage du genre Spotify de
    // l'artiste·e invité·e) matchait avec Renaud, incohérent à l'écoute malgré le tag partagé.
    // Seuil relatif (pas une liste de genres en dur) : s'adapte automatiquement si le catalogue
    // évolue. Les tags manuels (Tags, ex. "disney") restent eux non filtrés — posés à la main,
    // ils sont par construction fiables et jamais génériques au point de fausser un distracteur.
    private const double SeuilPartFrequenceGenreGenerique = 0.08;

    private static Dictionary<string, int> ConstruireFrequenceGenres(IReadOnlyList<Track> pool)
    {
        var frequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in pool)
        foreach (var genre in t.Genres)
            frequence[genre] = frequence.GetValueOrDefault(genre) + 1;
        return frequence;
    }

    private static bool PartageGenreOuTag(Track a, Track b, Dictionary<string, int> frequenceGenres, int tailleCatalogue)
    {
        bool EstSignificatif(string genre) =>
            frequenceGenres.TryGetValue(genre, out var n) && (double)n / tailleCatalogue <= SeuilPartFrequenceGenreGenerique;

        var genresA = a.Genres.Where(EstSignificatif);
        var genresB = b.Genres.Where(EstSignificatif);
        return genresA.Intersect(genresB, StringComparer.OrdinalIgnoreCase).Any()
            || a.Tags.Intersect(b.Tags, StringComparer.OrdinalIgnoreCase).Any();
    }

    /// <summary>Complète <paramref name="distracteurs"/> jusqu'à <paramref name="cible"/> en tirant
    /// sans remise dans <paramref name="pool"/>, en excluant les IDs et artistes déjà choisis. Le
    /// filtre est ré-appliqué après chaque tirage : un candidat restant peut partager l'artiste
    /// qu'on vient de sélectionner.</summary>
    private static void CompleterDepuisPool(List<Track> distracteurs, IReadOnlyList<Track> pool, int cible,
        HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random, Func<Track, bool> filtre)
    {
        bool EstEligible(Track t) => !idsChoisis.Contains(t.Id) && !artistesChoisis.Contains(t.Artist) && filtre(t);

        var disponibles = pool.Where(EstEligible).ToList();
        while (distracteurs.Count < cible && disponibles.Count > 0)
        {
            var index = random.Next(disponibles.Count);
            var track = disponibles[index];
            distracteurs.Add(track);
            idsChoisis.Add(track.Id);
            artistesChoisis.Add(track.Artist);
            disponibles.RemoveAt(index);
            disponibles = disponibles.Where(EstEligible).ToList();
        }
    }

    /// <summary>Prend, un par un, le(s) morceau(x) dont l'année est la plus proche de
    /// <paramref name="anneeCorrecte"/> (égalité départagée au hasard), en ré-excluant après chaque
    /// tirage — un candidat restant peut partager l'artiste qu'on vient de sélectionner.</summary>
    private static void CompleterParAnneeProche(List<Track> distracteurs, IReadOnlyList<Track> pool, int cible,
        HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random, int anneeCorrecte)
    {
        bool EstEligible(Track t) => !idsChoisis.Contains(t.Id) && !artistesChoisis.Contains(t.Artist) && t.Year is not null;

        while (distracteurs.Count < cible)
        {
            var candidats = pool.Where(EstEligible).ToList();
            if (candidats.Count == 0) return;

            var meilleurEcart = candidats.Min(t => Math.Abs(t.Year!.Value - anneeCorrecte));
            var plusProches = candidats.Where(t => Math.Abs(t.Year!.Value - anneeCorrecte) == meilleurEcart).ToList();
            var choisi = plusProches[random.Next(plusProches.Count)];

            distracteurs.Add(choisi);
            idsChoisis.Add(choisi.Id);
            artistesChoisis.Add(choisi.Artist);
        }
    }

    private static void Choisir(List<Track> distracteurs, List<Track> candidats, HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random)
    {
        var track = candidats[random.Next(candidats.Count)];
        distracteurs.Add(track);
        idsChoisis.Add(track.Id);
        artistesChoisis.Add(track.Artist);
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
