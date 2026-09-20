using Blindify.Domain.Configuration;

namespace Blindify.Api.Contracts;

public record TeamDto(string Id, string Nom);

/// <summary>NomsEquipes : uniquement pris en compte si ModeEquipe est actif — une Team est créée
/// par nom fourni. Ignoré (aucune équipe créée) si ModeEquipe est false.
/// Volontairement minimal depuis le retour utilisateur du 2026-08-24 : crée seulement le lobby
/// (code, hostSecret, équipes) — SANS configuration de blindtest (séries/tags/rounds), fournie
/// séparément par ConfigurerPartie, rappelable tant que la partie n'a pas démarré. Objectif :
/// permettre de recréer une configuration ratée sans recréer le lobby (ce qui forçait les joueurs
/// à quitter et rouvrir l'app Flutter faute d'un moyen de rejoindre une nouvelle partie).</summary>
public record CreateGameRequestDto(bool ModeEquipe, List<string>? NomsEquipes = null);

/// <summary>HostSecret : à conserver uniquement côté client host (jamais diffusé aux joueurs) —
/// requis par RejoinAsHost pour reprendre le contrôle host après un refresh/crash de l'onglet.</summary>
public record CreateGameResultDto(string Code, List<TeamDto> Teams, string HostSecret);

/// <summary>Configure (ou reconfigure entièrement) le blindtest d'une partie déjà créée — remplace
/// toute configuration précédente (recalcule la sélection de morceaux depuis zéro à chaque appel).
/// Refusé par GameHub.ConfigurerPartie si la partie a déjà quitté l'état Lobby.
///
/// Intention plutôt que résultat déjà calculé (retour utilisateur, docs/refactor-decisions.md
/// section 1) : le client fournit nombre de séries/rounds/durée + vivier de thèmes, le serveur
/// répartit les thèmes par série (SeriesPlanner.AssignerThemesAuxSeries), calcule les paliers de
/// mise bonus (SeriesPlanner.PaliersPourSerie) et tire les modes de round
/// (SeriesPlanner.PickRandomRoundModes) — logique auparavant dupliquée et non testée côté
/// host/app.js. Les 6 derniers champs reprennent les valeurs jusqu'ici codées en dur côté JS, non
/// exposées dans l'écran de configuration actuel.</summary>
public record ConfigurerPartieRequestDto(
    int NombreSeries,
    int NombreRoundsClassiques,
    int DureeFenetreReponseMs,
    List<string> ThemesVivier,
    GameConfig? Config,
    int PointsMax = 100,
    int PointsMin = 20,
    double PenaliteMauvaiseReponseRatio = 0.5,
    int PenaliteAbsenceReponse = -2,
    int DureePhaseMiseMs = 15000,
    int DureePhaseQuestionMs = 20000);
