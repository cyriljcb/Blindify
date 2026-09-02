using System.Text.Json;
using Blindify.Api.Contracts;
using Blindify.Domain.Enums;

namespace Blindify.Tests.Contracts;

/// <summary>Filet de non-régression sur le contrat SignalR (docs/refactor-decisions.md section 7) :
/// sérialise une instance représentative de chaque DTO de Blindify.Api.Contracts avec les options
/// JSON réellement utilisées par le hub (ContractJsonOptions — camelCase + enums en string) et
/// compare au JSON attendu, écrit en dur. Un renommage/retrait de champ change la sortie et fait
/// échouer le test correspondant avec un diff clair, plutôt qu'une désynchronisation silencieuse
/// avec les modèles Dart (app/lib/models/) et les handlers JS (host/app.js, host/display.js).
/// Pas de génération de code (décision explicite, coût jugé disproportionné pour un contrat qui
/// bouge quelques fois par an) — ce test ne supprime donc pas la triple copie du contrat, seulement
/// la désynchronisation silencieuse.</summary>
public class ContractsSerializationTests
{
    private static string Serialize<T>(T dto) => JsonSerializer.Serialize(dto, ContractJsonOptions.Instance);

    // ----- AdminContracts.cs -> pas de modèle Dart/JS dédié (appel fetch() direct côté host/app.js) -----

    [Fact]
    public void RestartRequestDto_Golden()
    {
        var json = Serialize(new RestartRequestDto("secret123"));
        Assert.Equal("""{"password":"secret123"}""", json);
    }

    // ----- CreateGameContracts.cs -> app/lib/models/join_result.dart (TeamDto), host/app.js:btn-create-game -----

    [Fact]
    public void TeamDto_Golden()
    {
        var json = Serialize(new TeamDto("team-1", "Rouge"));
        Assert.Equal("""{"id":"team-1","nom":"Rouge"}""", json);
    }

    [Fact]
    public void CreateGameRequestDto_Golden()
    {
        var json = Serialize(new CreateGameRequestDto(true, ["Rouge", "Bleu"]));
        Assert.Equal("""{"modeEquipe":true,"nomsEquipes":["Rouge","Bleu"]}""", json);
    }

    [Fact]
    public void CreateGameResultDto_Golden()
    {
        var dto = new CreateGameResultDto("ABCDE", [new TeamDto("team-1", "Rouge")], "secret-opaque");
        var json = Serialize(dto);
        Assert.Equal("""{"code":"ABCDE","teams":[{"id":"team-1","nom":"Rouge"}],"hostSecret":"secret-opaque"}""", json);
    }

    // ----- ConfigurerPartieRequestDto -> host/app.js:buildConfigurerPartieRequest, host/config.js -----

    [Fact]
    public void ConfigurerPartieRequestDto_Golden()
    {
        var dto = new ConfigurerPartieRequestDto(
            NombreSeries: 3,
            NombreRoundsClassiques: 8,
            DureeFenetreReponseMs: 20000,
            ThemesVivier: ["rock", "pop"],
            Config: null);
        var json = Serialize(dto);
        Assert.Equal(
            """{"nombreSeries":3,"nombreRoundsClassiques":8,"dureeFenetreReponseMs":20000,"themesVivier":["rock","pop"],"config":null,"pointsMax":100,"pointsMin":20,"penaliteMauvaiseReponseRatio":0.5,"penaliteAbsenceReponse":-5,"dureePhaseMiseMs":15000,"dureePhaseQuestionMs":20000}""",
            json);
    }

    // ----- HostStateSnapshotDto.cs -> host/app.js (RejoinAsHost, non encore appelé automatiquement) -----

    [Fact]
    public void HostStateSnapshotDto_Golden()
    {
        var dto = new HostStateSnapshotDto(false, RoundMode.Qcm, RoundCible.Titre, "t1", "audio/t1.mp3", 30000, 5200, 20000);
        var json = Serialize(dto);
        Assert.Equal(
            """{"enPause":false,"modeCourant":"Qcm","cibleCourante":"Titre","trackId":"t1","filePath":"audio/t1.mp3","refrainStartMs":30000,"positionAudioMs":5200,"dureeFenetreReponseMs":20000}""",
            json);
    }

    // ----- JoinContracts.cs -> app/lib/models/join_result.dart, app/lib/models/team.dart -----

    [Fact]
    public void PlayerSummaryDto_Golden()
    {
        var json = Serialize(new PlayerSummaryDto("player-1", "Alice", true, "team-1"));
        Assert.Equal("""{"playerId":"player-1","nom":"Alice","estConnecte":true,"teamId":"team-1"}""", json);
    }

    [Fact]
    public void JoinGameResultDto_Golden()
    {
        var dto = new JoinGameResultDto(true, null, 120, "team-1",
            [new TeamDto("team-1", "Rouge")],
            [new PlayerSummaryDto("player-1", "Alice", true, "team-1")]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"success":true,"errorMessage":null,"score":120,"teamId":"team-1","teams":[{"id":"team-1","nom":"Rouge"}],"joueurs":[{"playerId":"player-1","nom":"Alice","estConnecte":true,"teamId":"team-1"}]}""",
            json);
    }

    [Fact]
    public void PlayerJoinedDto_Golden()
    {
        var json = Serialize(new PlayerJoinedDto("player-1", "Alice"));
        Assert.Equal("""{"playerId":"player-1","nom":"Alice"}""", json);
    }

    [Fact]
    public void PlayerConnectionChangedDto_Golden()
    {
        var json = Serialize(new PlayerConnectionChangedDto("player-1", false));
        Assert.Equal("""{"playerId":"player-1","estConnecte":false}""", json);
    }

    [Fact]
    public void PlayerTeamChangedDto_Golden()
    {
        var json = Serialize(new PlayerTeamChangedDto("player-1", "team-1"));
        Assert.Equal("""{"playerId":"player-1","teamId":"team-1"}""", json);
    }

    // ----- RoundContracts.cs -> app/lib/models/round_started.dart, round_ended.dart, qcm_option.dart;
    //         host/app.js:onRoundStarted/onRoundEnded (handlers.js après le refactor host) -----

    [Fact]
    public void QcmOptionDto_Golden()
    {
        var json = Serialize(new QcmOptionDto("t1", "Under the Sea", "Samuel E. Wright", "La Petite Sirene"));
        Assert.Equal("""{"trackId":"t1","title":"Under the Sea","artist":"Samuel E. Wright","film":"La Petite Sirene"}""", json);
    }

    [Fact]
    public void RoundStartedForHostDto_Golden()
    {
        var dto = new RoundStartedForHostDto(RoundMode.Qcm, RoundCible.Titre, "t1", "audio/t1.mp3", 30000, 20000,
            [new QcmOptionDto("t1", "Under the Sea", "Samuel E. Wright", "La Petite Sirene")]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"mode":"Qcm","cible":"Titre","trackId":"t1","filePath":"audio/t1.mp3","refrainStartMs":30000,"dureeFenetreReponseMs":20000,"qcmOptions":[{"trackId":"t1","title":"Under the Sea","artist":"Samuel E. Wright","film":"La Petite Sirene"}]}""",
            json);
    }

    [Fact]
    public void RoundStartedForPlayersDto_Golden()
    {
        var dto = new RoundStartedForPlayersDto(RoundMode.TapeReponse, RoundCible.Auteur, 20000, 2, null);
        var json = Serialize(dto);
        Assert.Equal("""{"mode":"TapeReponse","cible":"Auteur","dureeFenetreReponseMs":20000,"serieIndex":2,"qcmOptions":null}""", json);
    }

    [Fact]
    public void SubmitAnswerRequestDto_Golden()
    {
        var json = Serialize(new SubmitAnswerRequestDto("Let It Go"));
        Assert.Equal("""{"reponse":"Let It Go"}""", json);
    }

    [Fact]
    public void RoundAnswerResultDto_Golden()
    {
        var json = Serialize(new RoundAnswerResultDto(true, 85, 205));
        Assert.Equal("""{"estCorrecte":true,"points":85,"nouveauScore":205}""", json);
    }

    [Fact]
    public void RoundResultEntryDto_Golden()
    {
        var json = Serialize(new RoundResultEntryDto("player-1", "Let It Go", true, 85));
        Assert.Equal("""{"playerId":"player-1","reponse":"Let It Go","estCorrecte":true,"points":85}""", json);
    }

    [Fact]
    public void RoundEndedDto_Golden()
    {
        var dto = new RoundEndedDto("t3", "Let It Go", "Idina Menzel", "covers/t3.jpg", RoundCible.Titre, "?",
            [new RoundResultEntryDto("player-1", "Let It Go", true, 85)]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t3","title":"Let It Go","artist":"Idina Menzel","coverPath":"covers/t3.jpg","cible":"Titre","film":"?","resultats":[{"playerId":"player-1","reponse":"Let It Go","estCorrecte":true,"points":85}]}""",
            json);
    }

    // ----- ScoreContracts.cs -> app/lib/models/score_update.dart, host/app.js:renderScoreList -----

    [Fact]
    public void PlayerScoreDto_Golden()
    {
        var json = Serialize(new PlayerScoreDto("player-1", "Alice", 205, "team-1"));
        Assert.Equal("""{"playerId":"player-1","nom":"Alice","score":205,"teamId":"team-1"}""", json);
    }

    [Fact]
    public void TeamScoreDto_Golden()
    {
        var json = Serialize(new TeamScoreDto("team-1", "Rouge", 340));
        Assert.Equal("""{"teamId":"team-1","nom":"Rouge","score":340}""", json);
    }

    [Fact]
    public void ScoreUpdateDto_Golden()
    {
        var dto = new ScoreUpdateDto([new PlayerScoreDto("player-1", "Alice", 205, "team-1")], [new TeamScoreDto("team-1", "Rouge", 340)]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"joueurs":[{"playerId":"player-1","nom":"Alice","score":205,"teamId":"team-1"}],"equipes":[{"teamId":"team-1","nom":"Rouge","score":340}]}""",
            json);
    }

    // ----- SeriesContracts.cs -> app/lib/models/serie_annoncee.dart, host/app.js:AnnoncerSerieCourante -----

    [Fact]
    public void SerieAnnonceeDto_Golden()
    {
        var json = Serialize(new SerieAnnonceeDto(1, ["rock", "annees-1990"]));
        Assert.Equal("""{"serieIndex":1,"tags":["rock","annees-1990"]}""", json);
    }

    // ----- ValidateAnswerManuallyRequestDto.cs -> host/app.js:validerManuellement -----

    [Fact]
    public void ValidateAnswerManuallyRequestDto_Golden()
    {
        var json = Serialize(new ValidateAnswerManuallyRequestDto("player-1", true));
        Assert.Equal("""{"playerId":"player-1","estCorrecte":true}""", json);
    }

    // ----- BonusContracts.cs -> app/lib/models/bonus_stake_options.dart, bonus_question_started.dart,
    //         bonus_result.dart; host/app.js:onBonusStakeOptions/onBonusQuestionStarted/onBonusResult -----

    [Fact]
    public void BonusStakeOptionsDto_Golden()
    {
        var json = Serialize(new BonusStakeOptionsDto([10, 20, 30, 50], 15000, 0));
        Assert.Equal("""{"paliers":[10,20,30,50],"dureePhaseMiseMs":15000,"serieIndex":0}""", json);
    }

    [Fact]
    public void SelectStakeRequestDto_Golden()
    {
        var json = Serialize(new SelectStakeRequestDto(3));
        Assert.Equal("""{"palierIndex":3}""", json);
    }

    [Fact]
    public void BonusQuestionStartedForHostDto_Golden()
    {
        var dto = new BonusQuestionStartedForHostDto("t1", "audio/t1.mp3", 30000, 20000, true, 0.65, RoundMode.Qcm,
            [new QcmOptionDto("t1", "Under the Sea", "Samuel E. Wright", "La Petite Sirene")], false);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t1","filePath":"audio/t1.mp3","refrainStartMs":30000,"dureePhaseQuestionMs":20000,"ralentissementActive":true,"facteurRalentissement":0.65,"mode":"Qcm","qcmOptions":[{"trackId":"t1","title":"Under the Sea","artist":"Samuel E. Wright","film":"La Petite Sirene"}],"estCourse":false}""",
            json);
    }

    [Fact]
    public void BonusQuestionStartedForPlayersDto_Golden()
    {
        var dto = new BonusQuestionStartedForPlayersDto(20000, RoundCible.Film, 0, RoundMode.PremiereLettre, null, true);
        var json = Serialize(dto);
        Assert.Equal("""{"dureePhaseQuestionMs":20000,"cible":"Film","serieIndex":0,"mode":"PremiereLettre","qcmOptions":null,"estCourse":true}""", json);
    }

    [Fact]
    public void SubmitBonusAnswerRequestDto_Golden()
    {
        var json = Serialize(new SubmitBonusAnswerRequestDto("Le Roi Lion"));
        Assert.Equal("""{"reponse":"Le Roi Lion"}""", json);
    }

    [Fact]
    public void BonusAnswerResultDto_Golden()
    {
        var json = Serialize(new BonusAnswerResultDto(true, 50, 255));
        Assert.Equal("""{"estCorrecte":true,"points":50,"nouveauScore":255}""", json);
    }

    [Fact]
    public void BonusResultEntryDto_Golden()
    {
        var json = Serialize(new BonusResultEntryDto("player-1", 50, "Le Roi Lion", true, 50));
        Assert.Equal("""{"playerId":"player-1","mise":50,"reponse":"Le Roi Lion","estCorrecte":true,"points":50}""", json);
    }

    [Fact]
    public void BonusResultDto_Golden()
    {
        var dto = new BonusResultDto("t2", "Circle of Life", "Elton John", "covers/t2.jpg", RoundCible.Film, "Le Roi Lion",
            [new BonusResultEntryDto("player-1", 50, "Le Roi Lion", true, 50)], false);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t2","title":"Circle of Life","artist":"Elton John","coverPath":"covers/t2.jpg","cible":"Film","film":"Le Roi Lion","resultats":[{"playerId":"player-1","mise":50,"reponse":"Le Roi Lion","estCorrecte":true,"points":50}],"estCourse":false}""",
            json);
    }
}
