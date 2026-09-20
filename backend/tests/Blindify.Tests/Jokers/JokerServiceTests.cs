using Blindify.Application.Answers;
using Blindify.Application.Jokers;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Jokers;

public class JokerServiceTests
{
    private readonly JokerService _service = new(new AnswerMatcher());

    private static Track NouveauTrack(string id, string title = "Un Titre Compose", string artist = "Premier Artiste, Featuring", int? year = null, List<string>? tags = null) => new()
    {
        Id = id,
        Title = title,
        Artist = artist,
        FilePath = $"audio/{id}.mp3",
        Year = year,
        Tags = tags ?? [],
    };

    [Fact]
    public void CalculerIndice_QcmCibleNormale_RetireDeuxDesTroisMauvaisesOptionsJamaisLaBonne()
    {
        var track = NouveauTrack("correct");
        var options = new List<RoundOption>
        {
            new() { TrackId = "correct", TexteAffiche = "Correct", EstFeinte = false, EstPiege = false },
            new() { TrackId = "faux1", TexteAffiche = "Faux 1", EstFeinte = false, EstPiege = false },
            new() { TrackId = "faux2", TexteAffiche = "Faux 2", EstFeinte = false, EstPiege = false },
            new() { TrackId = "faux3", TexteAffiche = "Faux 3", EstFeinte = false, EstPiege = false },
        };

        var indice = _service.CalculerIndice(RoundMode.Qcm, RoundCible.Titre, track, options, null);

        Assert.NotNull(indice.OptionsRetirees);
        Assert.Equal(2, indice.OptionsRetirees!.Count);
        Assert.DoesNotContain("correct", indice.OptionsRetirees);
        Assert.All(indice.OptionsRetirees, id => Assert.Contains(id, new[] { "faux1", "faux2", "faux3" }));
        Assert.Equal(indice.OptionsRetirees.Count, indice.OptionsRetirees.Distinct().Count());
    }

    [Fact]
    public void CalculerIndice_QcmCibleAnnee_RetireDeuxDesTroisMauvaisesAnneesJamaisLaBonne()
    {
        var track = NouveauTrack("t1", year: 1986);
        var anneeOptions = new List<int> { 1980, 1983, 1986, 1990 };

        var indice = _service.CalculerIndice(RoundMode.Qcm, RoundCible.Annee, track, null, anneeOptions);

        Assert.NotNull(indice.OptionsRetirees);
        Assert.Equal(2, indice.OptionsRetirees!.Count);
        Assert.DoesNotContain("1986", indice.OptionsRetirees);
        Assert.All(indice.OptionsRetirees, a => Assert.Contains(a, new[] { "1980", "1983", "1990" }));
    }

    [Fact]
    public void CalculerIndice_PremiereLettreTitre_QuatreLettresDistinctesIncluantLaBonne()
    {
        var track = NouveauTrack("t1", title: "Bohemian Rhapsody");

        var indice = _service.CalculerIndice(RoundMode.PremiereLettre, RoundCible.Titre, track, null, null);

        Assert.NotNull(indice.TuilesRestantes);
        Assert.Equal(4, indice.TuilesRestantes!.Count);
        Assert.Equal(4, indice.TuilesRestantes.Distinct().Count());
        Assert.Contains("B", indice.TuilesRestantes);
    }

    [Fact]
    public void CalculerIndice_PremiereLettreAuteur_UtiliseLePremierAuteurSeulement()
    {
        // AuteurVariantes.Acceptables().First() donne le premier artiste listé — "Zzephyr" ici, jamais
        // "Autre" (voir RoundService.AuteurVariantes, même convention que le libellé QCM côté Flutter :
        // un seul champ affiché, jamais tous les featurings). "Zzephyr" est choisi pour que sa lettre
        // ("Z") ne puisse jamais être elle-même tirée parmi les 3 lettres de remplissage.
        var track = NouveauTrack("t1", artist: "Zzephyr, Autre");

        var indice = _service.CalculerIndice(RoundMode.PremiereLettre, RoundCible.Auteur, track, null, null);

        Assert.NotNull(indice.TuilesRestantes);
        Assert.Contains("Z", indice.TuilesRestantes);
    }

    [Fact]
    public void CalculerIndice_TapeReponseTitre_MasqueLesLettresPreserveEspacesEtPonctuation()
    {
        var track = NouveauTrack("t1", title: "Don't Stop, Believin'!");

        var indice = _service.CalculerIndice(RoundMode.TapeReponse, RoundCible.Titre, track, null, null);

        Assert.Equal("___'_ ____, ________'!", indice.Structure);
        Assert.Null(indice.Decennie);
        Assert.Null(indice.TuilesRestantes);
        Assert.Null(indice.OptionsRetirees);
    }

    [Fact]
    public void CalculerIndice_TapeReponseFilm_MasqueLeNomDeFilmResolu()
    {
        // FilmNameResolver.Resoudre retombe sur Album nettoyé faute de motif explicite dans Title —
        // voir FilmNameResolverTests pour le détail de cette résolution, pas reproduit ici.
        var track = NouveauTrack("t1", title: "Under the Sea");
        track.Album = "The Little Mermaid";

        var indice = _service.CalculerIndice(RoundMode.TapeReponse, RoundCible.Film, track, null, null);

        Assert.NotNull(indice.Structure);
        Assert.DoesNotContain(indice.Structure, c => char.IsLetter(c));
    }

    [Fact]
    public void CalculerIndice_TapeReponseAnnee_RevelesLaDecennie()
    {
        var track = NouveauTrack("t1", year: 1986);

        var indice = _service.CalculerIndice(RoundMode.TapeReponse, RoundCible.Annee, track, null, null);

        Assert.Equal(1980, indice.Decennie);
        Assert.Null(indice.Structure);
    }
}
