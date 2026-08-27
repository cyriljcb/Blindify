using Blindify.Domain.Configuration;
using Blindify.Domain.Enums;

namespace Blindify.Api.Contracts;

public record TeamDto(string Id, string Nom);

/// <summary>Tags : thème de cette série uniquement (vide = tout le catalogue) — chaque série a son
/// propre thème depuis le retour utilisateur "je veux vraiment que la série deux ne concerne QUE
/// du rock", plus de thème global partagé par toute la partie (voir Series.Tags côté domaine).</summary>
public record SeriesSetupDto(SeriesConfig Config, List<RoundMode> RoundModes, List<string> Tags);

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
/// Refusé par GameHub.ConfigurerPartie si la partie a déjà quitté l'état Lobby.</summary>
public record ConfigurerPartieRequestDto(List<SeriesSetupDto> SeriesSetups, GameConfig? Config);
