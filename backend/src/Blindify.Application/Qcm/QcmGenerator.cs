using Blindify.Application.Rounds;
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
        // Un track peut être crédité "Angèle, Roméo Elvis" ou "Dua Lipa, Angèle" — comparer la
        // chaîne Artist telle quelle ne détecte pas qu'Angèle est déjà présente sous un autre
        // featuring (retour utilisateur : 3 options "Angèle" dans le même QCM, chacune via un
        // crédit différent). AuteurVariantes.Acceptables éclate le champ par personne créditée,
        // partagé avec la validation de réponse (RoundService) pour la même raison.
        var artistesChoisis = new HashSet<string>(AuteurVariantes.Acceptables(correct.Artist), StringComparer.OrdinalIgnoreCase);

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
                .Where(t => !idsChoisis.Contains(t.Id) && !PartageArtiste(t, artistesChoisis))
                .ToList();

            if (pieges.Count > 0)
                Choisir(distracteurs, pieges, idsChoisis, artistesChoisis, random);
        }

        // Évite si possible un distracteur du même auteur que la bonne réponse OU qu'un autre
        // distracteur déjà choisi (retour utilisateur : deux mauvaises réponses avec le même
        // artiste, illisible en cible Auteur puisqu'elles afficheraient alors un texte identique).
        // Best-effort seulement — voir filet de sécurité plus bas, jamais au prix de bloquer le
        // round faute d'options.
        //
        // Retour utilisateur : valider chaque distracteur INDÉPENDAMMENT ("partage au moins un tag
        // avec le bon morceau") permettait à un morceau tagué à la fois "pop" et "variete-francaise"
        // de ramener un distracteur anglais (via "pop") ET un distracteur français (via
        // "variete-francaise") sans qu'ils aient quoi que ce soit en commun ENTRE EUX (ex. Fall Out
        // Boy / Patrick Sébastien / Avicii dans le même QCM). On choisit maintenant UNE seule
        // "ancre" (le genre/tag significatif du bon morceau qui a le plus de candidats éligibles
        // dans le pool) et TOUS les distracteurs de ce palier doivent partager CETTE MÊME ancre —
        // garantit un ensemble cohérent entre eux, pas juste avec la bonne réponse chacun de son
        // côté. En complément, MemeStatutFrancophone empêche tout mélange français/anglais même via
        // une ancre partagée par les deux (ex. "pop").
        var frequenceGenres = ConstruireFrequenceGenres(pool);
        var frequenceTags = ConstruireFrequenceTags(pool);
        var ancre = ChoisirMeilleureAncre(correct, pool, frequenceGenres, frequenceTags, idsChoisis, artistesChoisis, random);
        if (ancre is not null)
        {
            CompleterDepuisPool(distracteurs, pool, NombreDistracteurs, idsChoisis, artistesChoisis, random,
                t => MemeStatutFrancophone(t, correct) && PartageAncrage(t, ancre));
        }

        // Repli intermédiaire : pas assez de morceaux du même genre/tag (catalogue trop niche, ou
        // morceau sans genre renseigné — ~40 % du catalogue actuel) -> on privilégie les morceaux de
        // l'année la plus proche plutôt que de sauter directement à un tirage totalement aléatoire.
        // Un tri par proximité (plutôt qu'un bucket "même décennie") évite l'effet de bord où deux
        // morceaux à un an d'écart (1999/2001) tombent dans des décennies différentes. La barrière
        // francophone reste appliquée ici (sinon elle referait surface via ce repli).
        if (distracteurs.Count < NombreDistracteurs && correct.Year is not null)
            CompleterParAnneeProche(distracteurs, pool, NombreDistracteurs, idsChoisis, artistesChoisis, random, correct);

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

    // Retour utilisateur : des titres français apparaissaient comme distracteurs d'un morceau
    // anglais (et inversement) — même avec une ancre partagée (ex. "pop"), il faut aussi que les
    // deux morceaux soient du même "monde" linguistique. Vérifié via le tag manuel
    // "variete-francaise" plutôt qu'un champ dédié (aucun champ langue dans le schéma).
    private const string TagVarieteFrancaise = "variete-francaise";

    private static Dictionary<string, int> ConstruireFrequenceGenres(IReadOnlyList<Track> pool)
    {
        var frequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in pool)
        foreach (var genre in t.Genres)
            frequence[genre] = frequence.GetValueOrDefault(genre) + 1;
        return frequence;
    }

    // Même principe que ConstruireFrequenceGenres, appliqué aux Tags manuels — nécessaire pour que
    // ChoisirMeilleureAncre écarte aussi les tags devenus trop génériques (ex. une décennie couvre
    // ~14 % du catalogue à elle seule, "variete-francaise" est également très répandu) sans les
    // lister en dur : le seuil relatif s'applique uniformément aux deux champs.
    private static Dictionary<string, int> ConstruireFrequenceTags(IReadOnlyList<Track> pool)
    {
        var frequence = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in pool)
        foreach (var tag in t.Tags)
            frequence[tag] = frequence.GetValueOrDefault(tag) + 1;
        return frequence;
    }

    private static bool MemeStatutFrancophone(Track a, Track b) =>
        a.Tags.Contains(TagVarieteFrancaise, StringComparer.OrdinalIgnoreCase) ==
        b.Tags.Contains(TagVarieteFrancaise, StringComparer.OrdinalIgnoreCase);

    private static bool PartageAncrage(Track t, string ancre) =>
        t.Genres.Contains(ancre, StringComparer.OrdinalIgnoreCase) || t.Tags.Contains(ancre, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> AncresSignificatives(Track t, Dictionary<string, int> frequenceGenres, Dictionary<string, int> frequenceTags, int tailleCatalogue)
    {
        bool EstSignificatif(Dictionary<string, int> frequence, string valeur) =>
            frequence.TryGetValue(valeur, out var n) && (double)n / tailleCatalogue <= SeuilPartFrequenceGenreGenerique;

        var genres = t.Genres.Where(g => EstSignificatif(frequenceGenres, g));
        var tags = t.Tags.Where(g => EstSignificatif(frequenceTags, g));
        return genres.Concat(tags).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Choisit, parmi les genres/tags significatifs de <paramref name="correct"/>, celui
    /// qui a le plus de candidats éligibles dans le pool (égalité départagée au hasard) — voir le
    /// commentaire dans GenererOptions sur pourquoi une ancre UNIQUE (pas "au moins un tag chacun")
    /// est nécessaire pour garantir un ensemble de distracteurs cohérent entre eux.</summary>
    private static string? ChoisirMeilleureAncre(Track correct, IReadOnlyList<Track> pool,
        Dictionary<string, int> frequenceGenres, Dictionary<string, int> frequenceTags,
        HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random)
    {
        var ancresCandidates = AncresSignificatives(correct, frequenceGenres, frequenceTags, pool.Count).ToList();
        if (ancresCandidates.Count == 0) return null;

        bool EstEligible(Track t) => !idsChoisis.Contains(t.Id) && !PartageArtiste(t, artistesChoisis) && MemeStatutFrancophone(t, correct);

        var meilleurCompte = 0;
        var meilleures = new List<string>();
        foreach (var ancre in ancresCandidates)
        {
            var compte = pool.Count(t => EstEligible(t) && PartageAncrage(t, ancre));
            if (compte > meilleurCompte)
            {
                meilleurCompte = compte;
                meilleures.Clear();
                meilleures.Add(ancre);
            }
            else if (compte > 0 && compte == meilleurCompte)
            {
                meilleures.Add(ancre);
            }
        }

        return meilleures.Count > 0 ? meilleures[random.Next(meilleures.Count)] : null;
    }

    /// <summary>Complète <paramref name="distracteurs"/> jusqu'à <paramref name="cible"/> en tirant
    /// sans remise dans <paramref name="pool"/>, en excluant les IDs et artistes déjà choisis. Le
    /// filtre est ré-appliqué après chaque tirage : un candidat restant peut partager l'artiste
    /// qu'on vient de sélectionner.</summary>
    private static void CompleterDepuisPool(List<Track> distracteurs, IReadOnlyList<Track> pool, int cible,
        HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random, Func<Track, bool> filtre)
    {
        bool EstEligible(Track t) => !idsChoisis.Contains(t.Id) && !PartageArtiste(t, artistesChoisis) && filtre(t);

        var disponibles = pool.Where(EstEligible).ToList();
        while (distracteurs.Count < cible && disponibles.Count > 0)
        {
            var index = random.Next(disponibles.Count);
            var track = disponibles[index];
            distracteurs.Add(track);
            idsChoisis.Add(track.Id);
            AjouterArtistes(track, artistesChoisis);
            disponibles.RemoveAt(index);
            disponibles = disponibles.Where(EstEligible).ToList();
        }
    }

    /// <summary>Prend, un par un, le(s) morceau(x) dont l'année est la plus proche de celle de
    /// <paramref name="correct"/> (égalité départagée au hasard), en ré-excluant après chaque
    /// tirage — un candidat restant peut partager l'artiste qu'on vient de sélectionner. La barrière
    /// francophone (MemeStatutFrancophone) reste appliquée à ce repli, sinon le mélange
    /// français/anglais y referait surface.</summary>
    private static void CompleterParAnneeProche(List<Track> distracteurs, IReadOnlyList<Track> pool, int cible,
        HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random, Track correct)
    {
        var anneeCorrecte = correct.Year!.Value;
        bool EstEligible(Track t) => !idsChoisis.Contains(t.Id) && !PartageArtiste(t, artistesChoisis)
            && t.Year is not null && MemeStatutFrancophone(t, correct);

        while (distracteurs.Count < cible)
        {
            var candidats = pool.Where(EstEligible).ToList();
            if (candidats.Count == 0) return;

            var meilleurEcart = candidats.Min(t => Math.Abs(t.Year!.Value - anneeCorrecte));
            var plusProches = candidats.Where(t => Math.Abs(t.Year!.Value - anneeCorrecte) == meilleurEcart).ToList();
            var choisi = plusProches[random.Next(plusProches.Count)];

            distracteurs.Add(choisi);
            idsChoisis.Add(choisi.Id);
            AjouterArtistes(choisi, artistesChoisis);
        }
    }

    private static void Choisir(List<Track> distracteurs, List<Track> candidats, HashSet<string> idsChoisis, HashSet<string> artistesChoisis, Random random)
    {
        var track = candidats[random.Next(candidats.Count)];
        distracteurs.Add(track);
        idsChoisis.Add(track.Id);
        AjouterArtistes(track, artistesChoisis);
    }

    private static bool PartageArtiste(Track t, HashSet<string> artistesChoisis) =>
        AuteurVariantes.Acceptables(t.Artist).Any(artistesChoisis.Contains);

    private static void AjouterArtistes(Track t, HashSet<string> artistesChoisis)
    {
        foreach (var artiste in AuteurVariantes.Acceptables(t.Artist))
            artistesChoisis.Add(artiste);
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
