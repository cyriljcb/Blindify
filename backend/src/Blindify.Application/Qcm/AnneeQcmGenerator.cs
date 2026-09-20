namespace Blindify.Application.Qcm;

public class AnneeQcmGenerator : IAnneeQcmGenerator
{
    private const int NombreOptions = 4;
    private const int EcartMinEntreOptions = 2;
    private const int EcartMaxAvecCorrecte = 10;

    public List<int> GenererOptions(int anneeCorrecte, Random random)
    {
        var anneeMax = DateTime.UtcNow.Year;

        // Position de la bonne réponse tirée AVANT de générer les distracteurs (retour utilisateur :
        // sinon elle tombe trop souvent au milieu et les joueurs l'apprennent) — nombreInferieurs
        // distracteurs plus petits, le reste plus grands, tous ajoutés puis triés ensemble.
        var position = random.Next(NombreOptions);
        var nombreInferieurs = position;
        var nombreSuperieurs = NombreOptions - 1 - position;

        var inferieurs = GenererCote(anneeCorrecte, nombreInferieurs, versLeBas: true, plafondAbsolu: null, random);
        var superieurs = GenererCote(anneeCorrecte, nombreSuperieurs, versLeBas: false, plafondAbsolu: anneeMax, random);

        var toutes = new List<int>(inferieurs) { anneeCorrecte };
        toutes.AddRange(superieurs);
        toutes.Sort();
        return toutes;
    }

    /// <summary>Génère `nombre` années du même côté (plus petites ou plus grandes) de anneeCorrecte,
    /// en cumulant l'écart à chaque option pour garantir au moins EcartMinEntreOptions entre elles
    /// (pas seulement avec anneeCorrecte), plafonné à EcartMaxAvecCorrecte et, côté "supérieur", à
    /// plafondAbsolu (jamais dans le futur). À chaque étape, réserve EcartMinEntreOptions pour
    /// chacune des options restantes de ce côté avant de tirer l'écart courant — sans cette réserve,
    /// un premier tirage trop généreux pouvait ne plus laisser assez de marge au suivant et produire
    /// un écart de 1 an au lieu de 2 (repéré par test). Filet de sécurité si le budget total est
    /// insuffisant même en réservant (morceau très récent, peu de marge vers le futur) : répète le
    /// dernier écart valide (option dupliquée) plutôt que de violer l'écart minimal ou de dépasser
    /// le plafond — même philosophie que QcmGenerator (repli plutôt que blocage).</summary>
    private static List<int> GenererCote(int anneeCorrecte, int nombre, bool versLeBas, int? plafondAbsolu, Random random)
    {
        var resultat = new List<int>();
        var ecart = 0;

        for (var i = 0; i < nombre; i++)
        {
            var ecartMaxGlobal = EcartMaxAvecCorrecte;
            if (plafondAbsolu is not null) ecartMaxGlobal = Math.Min(ecartMaxGlobal, Math.Max(0, plafondAbsolu.Value - anneeCorrecte));

            var reserve = (nombre - 1 - i) * EcartMinEntreOptions;
            var ecartMaxCetteEtape = ecartMaxGlobal - reserve;
            var ecartMin = ecart + EcartMinEntreOptions;

            ecart = ecartMin > ecartMaxCetteEtape
                ? Math.Min(Math.Max(ecart, 0), ecartMaxGlobal)
                : random.Next(ecartMin, ecartMaxCetteEtape + 1);

            resultat.Add(versLeBas ? anneeCorrecte - ecart : anneeCorrecte + ecart);
        }

        return resultat;
    }
}
