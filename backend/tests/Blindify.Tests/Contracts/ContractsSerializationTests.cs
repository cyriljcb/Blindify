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
            """{"nombreSeries":3,"nombreRoundsClassiques":8,"dureeFenetreReponseMs":20000,"themesVivier":["rock","pop"],"config":null,"pointsMax":100,"pointsMin":20,"penaliteMauvaiseReponseRatio":0.5,"penaliteAbsenceReponse":-2,"dureePhaseMiseMs":15000,"dureePhaseQuestionMs":20000}""",
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
            [new PlayerSummaryDto("player-1", "Alice", true, "team-1")],
            null);
        var json = Serialize(dto);
        Assert.Equal(
            """{"success":true,"errorMessage":null,"score":120,"teamId":"team-1","teams":[{"id":"team-1","nom":"Rouge"}],"joueurs":[{"playerId":"player-1","nom":"Alice","estConnecte":true,"teamId":"team-1"}],"etatCourant":null}""",
            json);
    }

    [Fact]
    public void EtatCourantJoueurDto_Golden_RoundClassique()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var round = new RoundStartedForPlayersDto(RoundMode.TapeReponse, RoundCible.Auteur, roundId, 20000, 2, null, TempsEcouleMs: 4200);
        var dto = new EtatCourantJoueurDto(PhaseJoueur.RoundClassique, false, true, round, null, null);
        var json = Serialize(dto);
        Assert.Equal(
            """{"phase":"RoundClassique","enPause":false,"dejaRepondu":true,"round":{"mode":"TapeReponse","cible":"Auteur","roundId":"00000000-0000-0000-0000-000000000001","dureeFenetreReponseMs":20000,"serieIndex":2,"qcmOptions":null,"tempsEcouleMs":4200,"anneeOptions":null},"bonusMise":null,"bonusQuestion":null}""",
            json);
    }

    [Fact]
    public void EtatCourantJoueurDto_Golden_BonusMise()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var bonusMise = new BonusStakeOptionsDto([10, 20, 30, 50], roundId, 15000, 0, TempsEcouleMs: 3000);
        var dto = new EtatCourantJoueurDto(PhaseJoueur.BonusMise, true, false, null, bonusMise, null);
        var json = Serialize(dto);
        Assert.Equal(
            """{"phase":"BonusMise","enPause":true,"dejaRepondu":false,"round":null,"bonusMise":{"paliers":[10,20,30,50],"roundId":"00000000-0000-0000-0000-000000000002","dureePhaseMiseMs":15000,"serieIndex":0,"tempsEcouleMs":3000},"bonusQuestion":null}""",
            json);
    }

    [Fact]
    public void EtatCourantJoueurDto_Golden_Aucune()
    {
        var dto = new EtatCourantJoueurDto(PhaseJoueur.Aucune, false, false, null, null, null);
        var json = Serialize(dto);
        Assert.Equal(
            """{"phase":"Aucune","enPause":false,"dejaRepondu":false,"round":null,"bonusMise":null,"bonusQuestion":null}""",
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
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var dto = new RoundStartedForHostDto(RoundMode.Qcm, RoundCible.Titre, roundId, "t1", "audio/t1.mp3", 30000, 20000,
            [new QcmOptionDto("t1", "Under the Sea", "Samuel E. Wright", "La Petite Sirene")]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"mode":"Qcm","cible":"Titre","roundId":"00000000-0000-0000-0000-000000000003","trackId":"t1","filePath":"audio/t1.mp3","refrainStartMs":30000,"dureeFenetreReponseMs":20000,"qcmOptions":[{"trackId":"t1","title":"Under the Sea","artist":"Samuel E. Wright","film":"La Petite Sirene"}],"anneeOptions":null}""",
            json);
    }

    [Fact]
    public void RoundStartedForPlayersDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var dto = new RoundStartedForPlayersDto(RoundMode.TapeReponse, RoundCible.Auteur, roundId, 20000, 2, null, TempsEcouleMs: 0);
        var json = Serialize(dto);
        Assert.Equal("""{"mode":"TapeReponse","cible":"Auteur","roundId":"00000000-0000-0000-0000-000000000004","dureeFenetreReponseMs":20000,"serieIndex":2,"qcmOptions":null,"tempsEcouleMs":0,"anneeOptions":null}""", json);
    }

    [Fact]
    public void SubmitAnswerRequestDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var json = Serialize(new SubmitAnswerRequestDto(roundId, "Let It Go"));
        Assert.Equal("""{"roundId":"00000000-0000-0000-0000-000000000005","reponse":"Let It Go"}""", json);
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
        Assert.Equal("""{"playerId":"player-1","reponse":"Let It Go","estCorrecte":true,"points":85,"ecartAnnee":null}""", json);
    }

    [Fact]
    public void RoundEndedDto_Golden()
    {
        var dto = new RoundEndedDto("t3", "Let It Go", "Idina Menzel", "covers/t3.jpg", RoundCible.Titre, "?",
            [new RoundResultEntryDto("player-1", "Let It Go", true, 85)]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t3","title":"Let It Go","artist":"Idina Menzel","coverPath":"covers/t3.jpg","cible":"Titre","film":"?","resultats":[{"playerId":"player-1","reponse":"Let It Go","estCorrecte":true,"points":85,"ecartAnnee":null}],"annee":null}""",
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

    [Fact]
    public void TitreDto_Golden()
    {
        var json = Serialize(new TitreDto("ECLAIR", "Eclair rapide", "2,4 s en moyenne", ["player-1", "player-2"]));
        Assert.Equal("""{"code":"ECLAIR","libelle":"Eclair rapide","description":"2,4 s en moyenne","playerIds":["player-1","player-2"]}""", json);
    }

    [Fact]
    public void GameEndedDto_Golden()
    {
        var score = new ScoreUpdateDto([new PlayerScoreDto("player-1", "Alice", 205, "team-1")], null);
        var dto = new GameEndedDto(score, [new TitreDto("FIDELE", "Fidele", "A participe a toute la partie", ["player-1"])]);
        var json = Serialize(dto);
        Assert.Equal(
            """{"score":{"joueurs":[{"playerId":"player-1","nom":"Alice","score":205,"teamId":"team-1"}],"equipes":null},"titres":[{"code":"FIDELE","libelle":"Fidele","description":"A participe a toute la partie","playerIds":["player-1"]}]}""",
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
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000006");
        var json = Serialize(new BonusStakeOptionsDto([10, 20, 30, 50], roundId, 15000, 0, TempsEcouleMs: 0));
        Assert.Equal("""{"paliers":[10,20,30,50],"roundId":"00000000-0000-0000-0000-000000000006","dureePhaseMiseMs":15000,"serieIndex":0,"tempsEcouleMs":0}""", json);
    }

    [Fact]
    public void SelectStakeRequestDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000007");
        var json = Serialize(new SelectStakeRequestDto(roundId, 3));
        Assert.Equal("""{"roundId":"00000000-0000-0000-0000-000000000007","palierIndex":3}""", json);
    }

    [Fact]
    public void BonusQuestionStartedForHostDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000008");
        var dto = new BonusQuestionStartedForHostDto("t1", "audio/t1.mp3", 30000, roundId, 20000, true, 0.65, RoundMode.Qcm,
            [new QcmOptionDto("t1", "Under the Sea", "Samuel E. Wright", "La Petite Sirene")], false);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t1","filePath":"audio/t1.mp3","refrainStartMs":30000,"roundId":"00000000-0000-0000-0000-000000000008","dureePhaseQuestionMs":20000,"ralentissementActive":true,"facteurRalentissement":0.65,"mode":"Qcm","qcmOptions":[{"trackId":"t1","title":"Under the Sea","artist":"Samuel E. Wright","film":"La Petite Sirene"}],"estCourse":false,"anneeOptions":null}""",
            json);
    }

    [Fact]
    public void BonusQuestionStartedForPlayersDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-000000000009");
        var dto = new BonusQuestionStartedForPlayersDto(roundId, 20000, RoundCible.Film, 0, RoundMode.PremiereLettre, null, true, TempsEcouleMs: 0);
        var json = Serialize(dto);
        Assert.Equal("""{"roundId":"00000000-0000-0000-0000-000000000009","dureePhaseQuestionMs":20000,"cible":"Film","serieIndex":0,"mode":"PremiereLettre","qcmOptions":null,"estCourse":true,"tempsEcouleMs":0,"anneeOptions":null}""", json);
    }

    [Fact]
    public void SubmitBonusAnswerRequestDto_Golden()
    {
        var roundId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var json = Serialize(new SubmitBonusAnswerRequestDto(roundId, "Le Roi Lion"));
        Assert.Equal("""{"roundId":"00000000-0000-0000-0000-00000000000a","reponse":"Le Roi Lion"}""", json);
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
        Assert.Equal("""{"playerId":"player-1","mise":50,"reponse":"Le Roi Lion","estCorrecte":true,"points":50,"ecartAnnee":null}""", json);
    }

    [Fact]
    public void BonusResultDto_Golden()
    {
        var dto = new BonusResultDto("t2", "Circle of Life", "Elton John", "covers/t2.jpg", RoundCible.Film, "Le Roi Lion",
            [new BonusResultEntryDto("player-1", 50, "Le Roi Lion", true, 50)], false);
        var json = Serialize(dto);
        Assert.Equal(
            """{"trackId":"t2","title":"Circle of Life","artist":"Elton John","coverPath":"covers/t2.jpg","cible":"Film","film":"Le Roi Lion","resultats":[{"playerId":"player-1","mise":50,"reponse":"Le Roi Lion","estCorrecte":true,"points":50,"ecartAnnee":null}],"estCourse":false,"annee":null}""",
            json);
    }

    // ----- FlagContracts.cs -> app (réglages admin), host/app.js (V2, section 12.4) -----

    [Fact]
    public void SignalementRequestDto_Golden()
    {
        var json = Serialize(new SignalementRequestDto("t1", RaisonSignalement.MauvaiseVersion, "version live"));
        Assert.Equal("""{"trackId":"t1","raison":"MauvaiseVersion","commentaire":"version live"}""", json);
    }

    [Fact]
    public void SignalementResultDto_Golden()
    {
        var json = Serialize(new SignalementResultDto("flag-1", false));
        Assert.Equal("""{"flagId":"flag-1","dejaSignale":false}""", json);
    }

    [Fact]
    public void MorceauSignaleDto_Golden()
    {
        var json = Serialize(new MorceauSignaleDto("t1", "Circle of Life", "Elton John", RaisonSignalement.AudioDefectueux));
        Assert.Equal("""{"trackId":"t1","titre":"Circle of Life","artiste":"Elton John","raison":"AudioDefectueux"}""", json);
    }
}
