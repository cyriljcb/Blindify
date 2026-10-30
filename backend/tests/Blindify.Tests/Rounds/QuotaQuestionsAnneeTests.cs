using Blindify.Application.Answers;
using Blindify.Application.Bonus;
using Blindify.Application.Jokers;
using Blindify.Application.Qcm;
using Blindify.Application.Rounds;
using Blindify.Application.Scoring;
using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Rounds;

public class QuotaQuestionsAnneeTests
{
    private readonly RoundService _service = new(new ScoringService(), new QcmGenerator(), new AnswerMatcher(), new AnneeQcmGenerator(), new JokerService(new AnswerMatcher()));
    private readonly BonusRoundService _bonusService = new(new BonusScoringService(), new AnswerMatcher(), new QcmGenerator(), new AnneeQcmGenerator());

    private static Track NouveauTrack(string id, int? year = 1995, List<string>? tags = null) => new()
    {
        Id = id,
        Title = $"Titre {id}",
        Artist = $"Artiste {id}",
        FilePath = $"audio/{id}.mp3",
        Year = year,
        Tags = tags ?? []
    };

    private static Series NouvelleSerie(IEnumerable<Track> tracks, List<string>? tags = null) => new()
    {
        Index = 0,
        Config = new SeriesConfig(),
        Tags = tags ?? [],
        Rounds = tracks.Select(t => new Round { TrackId = t.Id, Mode = RoundMode.TapeReponse }).ToList()
    };

    /// <summary>Joue une série complète comme GameHub : chaque round démarré avec la décision du
    /// quota, puis la question bonus. Retourne le nombre de questions Année posées.</summary>
    private int JouerSerie(List<Track> tracks, Track bonus, GameConfig config, Random random, List<string>? tags = null)
    {
        var parId = tracks.Append(bonus).ToDictionary(t => t.Id);
        var serie = NouvelleSerie(tracks, tags);
        for (var i = 0; i < serie.Rounds.Count; i++)
        {
            var forcer = QuotaQuestionsAnnee.PourRound(serie, i, id => parId.GetValueOrDefault(id), config, random);
            _service.DemarrerRound(serie.Rounds[i], parId[serie.Rounds[i].TrackId], tracks, serie.Tags, config, DateTimeOffset.UtcNow, forcer);
        }

        var forcerBonus = QuotaQuestionsAnnee.PourBonus(serie, bonus, config, random);
        var bonusRound = _bonusService.CreerBonusRound(bonus, tracks, serie.Tags, config, forcerBonus);
        return serie.Rounds.Count(r => r.Cible == RoundCible.Annee) + (bonusRound.Cible == RoundCible.Annee ? 1 : 0);
    }

    [Fact]
    public void SerieComplete_ExactementUneQuestionAnnee_BonusCompris()
    {
        var config = new GameConfig();
        var random = new Random(42);
        var tracks = Enumerable.Range(0, 10).Select(i => NouveauTrack($"t{i}")).ToList();

        for (var essai = 0; essai < 300; essai++)
            Assert.Equal(1, JouerSerie(tracks, NouveauTrack("bonus"), config, random));
    }

    [Fact]
    public void SerieComplete_LaQuestionAnneePeutTomberSurNimporteQuelCreneau_DontLeBonus()
    {
        var config = new GameConfig();
        var random = new Random(7);
        var tracks = Enumerable.Range(0, 4).Select(i => NouveauTrack($"t{i}")).ToList();
        var bonus = NouveauTrack("bonus");
        var positions = new HashSet<int>();

        for (var essai = 0; essai < 400; essai++)
        {
            var parId = tracks.Append(bonus).ToDictionary(t => t.Id);
            var serie = NouvelleSerie(tracks);
            for (var i = 0; i < serie.Rounds.Count; i++)
            {
                var forcer = QuotaQuestionsAnnee.PourRound(serie, i, id => parId.GetValueOrDefault(id), config, random);
                _service.DemarrerRound(serie.Rounds[i], parId[serie.Rounds[i].TrackId], tracks, serie.Tags, config, DateTimeOffset.UtcNow, forcer);
            }
            var index = serie.Rounds.FindIndex(r => r.Cible == RoundCible.Annee);
            positions.Add(index == -1 ? serie.Rounds.Count : index);
        }

        Assert.Equal(new HashSet<int> { 0, 1, 2, 3, 4 }, positions);
    }

    [Fact]
    public void SerieDisney_AucuneQuestionAnnee()
    {
        var config = new GameConfig();
        var random = new Random(1);
        var tracks = Enumerable.Range(0, 5).Select(i => NouveauTrack($"d{i}", tags: ["disney"])).ToList();

        for (var essai = 0; essai < 50; essai++)
            Assert.Equal(0, JouerSerie(tracks, NouveauTrack("bonus", tags: ["disney"]), config, random, tags: ["disney"]));
    }

    [Fact]
    public void SerieFilmSerie_AucuneQuestionAnnee()
    {
        var config = new GameConfig();
        var random = new Random(1);
        var tracks = Enumerable.Range(0, 5).Select(i => NouveauTrack($"f{i}", tags: ["film-serie"])).ToList();

        for (var essai = 0; essai < 50; essai++)
            Assert.Equal(0, JouerSerie(tracks, NouveauTrack("bonus", tags: ["film-serie"]), config, random, tags: ["film-serie"]));
    }

    [Fact]
    public void MorceauFilmSerieDouble_EligibleHorsThemeFilmSerie()
    {
        var track = NouveauTrack("eye", tags: ["film-serie", "rock"]);

        Assert.True(QuotaQuestionsAnnee.EstEligible(track, ["rock"]));
        Assert.False(QuotaQuestionsAnnee.EstEligible(track, ["film-serie"]));
        Assert.False(QuotaQuestionsAnnee.EstEligible(NouveauTrack("seul", tags: ["film-serie"]), []));
    }

    [Fact]
    public void MorceauxSansAnnee_SontSautes_LeQuotaTombeSurUnMorceauEligible()
    {
        var config = new GameConfig();
        var random = new Random(3);
        var tracks = Enumerable.Range(0, 6).Select(i => NouveauTrack($"t{i}", year: i == 5 ? 2001 : null)).ToList();

        for (var essai = 0; essai < 100; essai++)
            Assert.Equal(1, JouerSerie(tracks, NouveauTrack("bonus"), config, random));
    }

    [Fact]
    public void BonusFinalementIneligible_AucuneQuestionAnnee_PlutotQueDeLaForcer()
    {
        // Le morceau du bonus n'est connu qu'à StartBonusRound : compté éligible d'avance, il peut
        // s'avérer sans année — la série n'a alors pas de question Année (limite documentée dans
        // QuotaQuestionsAnnee), mais jamais plus d'une.
        var config = new GameConfig();
        var random = new Random(3);
        var tracks = Enumerable.Range(0, 6).Select(i => NouveauTrack($"t{i}", year: i == 5 ? 2001 : null)).ToList();

        var resultats = Enumerable.Range(0, 100).Select(_ => JouerSerie(tracks, NouveauTrack("bonus", year: null), config, random)).ToList();

        Assert.All(resultats, n => Assert.InRange(n, 0, 1));
    }

    [Fact]
    public void QuotaZero_JamaisDeQuestionAnnee()
    {
        var config = new GameConfig { QuestionsAnneeParSerie = 0 };
        var tracks = Enumerable.Range(0, 10).Select(i => NouveauTrack($"t{i}")).ToList();

        Assert.Equal(0, JouerSerie(tracks, NouveauTrack("bonus"), config, new Random(5)));
    }

    [Fact]
    public void QuotaNull_GardeLeTiragePondereHistorique()
    {
        Assert.Null(QuotaQuestionsAnnee.Decider(quota: null, dejaPosees: 0, creneauxEligiblesRestants: 5, eligible: true, new Random(0)));
    }

    [Fact]
    public void Decider_DernierCreneauEligible_ForceLAnnee()
    {
        Assert.True(QuotaQuestionsAnnee.Decider(quota: 1, dejaPosees: 0, creneauxEligiblesRestants: 1, eligible: true, new Random(0)));
    }

    [Fact]
    public void Decider_QuotaAtteint_ExclutLAnnee()
    {
        Assert.False(QuotaQuestionsAnnee.Decider(quota: 1, dejaPosees: 1, creneauxEligiblesRestants: 3, eligible: true, new Random(0)));
    }
}
