namespace Blindify.Application.Qcm;

public class AnneeQcmGenerator : IAnneeQcmGenerator
{
    private const int NombreOptionsParDefaut = 4;
    private const int EcartMinEntreOptions = 2;
    private const int EcartMaxAvecCorrecte = 10;

    public List<int> GenererOptions(int anneeCorrecte, Random random) =>
        GenererOptions(anneeCorrecte, random, NombreOptionsParDefaut);

    public List<int> GenererOptions(int anneeCorrecte, Random random, int nombreOptions)
    {
        nombreOptions = Math.Max(2, nombreOptions);
        var anneeMax = DateTime.UtcNow.Year;

        // Audit 2026-11-10 : la position de la bonne réponse était tirée sans tenir compte de la
        // marge vers le futur — pour un morceau de l'année (ou des 3 précédentes) le repli dupliquait
        // l'option, jusqu'à [2025, 2025, 2025, 2025]. On borne maintenant le nombre d'options
        // au-dessus par la place réellement disponible (EcartMinEntreOptions entre chacune, jamais
        // au-delà de l'année courante) ; le reste va en dessous, où la place ne manque jamais.
        var placeAuDessus = Math.Min(EcartMaxAvecCorrecte, Math.Max(0, anneeMax - anneeCorrecte)) / EcartMinEntreOptions;
        var placeEnDessous = EcartMaxAvecCorrecte / EcartMinEntreOptions;

        // Position de la bonne réponse tirée AVANT de générer les distracteurs (retour utilisateur :
        // sinon elle tombe trop souvent au milieu et les joueurs l'apprennent), parmi les positions
        // compatibles avec la place disponible de chaque côté.
        var minSuperieurs = Math.Max(0, nombreOptions - 1 - placeEnDessous);
        var maxSuperieurs = Math.Min(nombreOptions - 1, placeAuDessus);
        var nombreSuperieurs = maxSuperieurs >= minSuperieurs ? random.Next(minSuperieurs, maxSuperieurs + 1) : maxSuperieurs;
        var nombreInferieurs = nombreOptions - 1 - nombreSuperieurs;

        var inferieurs = GenererCote(anneeCorrecte, nombreInferieurs, versLeBas: true, EcartMaxAvecCorrecte, random);
        var superieurs = GenererCote(anneeCorrecte, nombreSuperieurs, versLeBas: false, Math.Min(EcartMaxAvecCorrecte, anneeMax - anneeCorrecte), random);

        var toutes = new List<int>(inferieurs) { anneeCorrecte };
        toutes.AddRange(superieurs);
        return toutes.Distinct().OrderBy(a => a).ToList();
    }

    /// <summary>Génère `nombre` années du même côté (plus petites ou plus grandes) de anneeCorrecte,
    /// en cumulant l'écart à chaque option pour garantir au moins EcartMinEntreOptions entre elles
    /// (pas seulement avec anneeCorrecte), sans dépasser ecartMax. À chaque étape, réserve
    /// EcartMinEntreOptions pour chacune des options restantes de ce côté avant de tirer l'écart
    /// courant — sans cette réserve, un premier tirage trop généreux pouvait ne plus laisser assez de
    /// marge au suivant. L'appelant garantit que nombre × EcartMinEntreOptions ≤ ecartMax.</summary>
    private static List<int> GenererCote(int anneeCorrecte, int nombre, bool versLeBas, int ecartMax, Random random)
    {
        var resultat = new List<int>();
        var ecart = 0;

        for (var i = 0; i < nombre; i++)
        {
            var reserve = (nombre - 1 - i) * EcartMinEntreOptions;
            var ecartMaxCetteEtape = ecartMax - reserve;
            var ecartMin = ecart + EcartMinEntreOptions;

            ecart = ecartMin > ecartMaxCetteEtape ? ecartMin : random.Next(ecartMin, ecartMaxCetteEtape + 1);
            resultat.Add(versLeBas ? anneeCorrecte - ecart : anneeCorrecte + ecart);
        }

        return resultat;
    }
}
