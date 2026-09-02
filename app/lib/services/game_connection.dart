import 'dart:async';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:signalr_netcore/signalr_client.dart';

import '../models/bonus_question_started.dart';
import '../models/bonus_result.dart';
import '../models/bonus_stake_options.dart';
import '../models/join_result.dart';
import '../models/round_ended.dart';
import '../models/round_started.dart';
import '../models/score_update.dart';
import '../models/serie_annoncee.dart';
import '../models/team.dart';

enum AppScreen { loading, connect, join, lobby, serieIntro, round, roundEnded, bonusStake, bonusCourseIntro, bonusQuestion, bonusResult, ended }

class PlayerInfo {
  PlayerInfo({required this.playerId, required this.nom, this.estConnecte = true, this.teamId});

  final String playerId;
  final String nom;
  bool estConnecte;
  String? teamId;
}

const _prefsPlayerId = 'blindify_player_id';
const _prefsServerUrl = 'blindify_server_url';
const _prefsNom = 'blindify_nom';

/// État central de l'app — un seul HubConnection, mirroring volontaire du pattern
/// déjà validé côté host (`host/app.js`) : état global simple, écran courant piloté
/// par les événements reçus du serveur plutôt qu'un Navigator.
// Retour utilisateur : le mode course (voir BonusQuestionStarted.estCourse) passait inaperçu,
// simple bannière au milieu de l'écran de question. Un écran dédié forcé pendant ce délai est
// impossible à manquer, même en tapant vite sur la question précédente.
const _dureeIntroCourse = Duration(milliseconds: 2500);

class GameConnection extends ChangeNotifier {
  HubConnection? _hub;
  SharedPreferences? _prefs;
  Timer? _introCourseTimer;

  String? playerId;
  String? serverUrl;
  String? nom;
  String? gameCode;

  /// Code de partie extrait d'un QR scanné (voir QrScanScreen), en attente d'être consommé par
  /// JoinScreen pour pré-remplir son champ — remis à null après lecture pour ne pas re-préremplir
  /// un futur passage sur cet écran (ex. après une partie terminée, code manuel suivant).
  String? pendingJoinCode;

  /// URL complète d'une pochette (servie sous /files, comme l'audio côté host — voir
  /// Program.cs). null si le morceau n'a pas de coverPath.
  String? coverUrl(String? coverPath) => coverPath == null ? null : '$serverUrl/files/$coverPath';

  AppScreen screen = AppScreen.loading;
  bool connected = false;
  bool connecting = false;
  String? errorMessage;

  final List<PlayerInfo> players = [];
  int score = 0;
  String? teamId;
  List<Team> teams = []; // équipes disponibles — vide si mode équipe inactif

  SerieAnnoncee? serieIntro;

  RoundStarted? currentRound;
  bool roundAnswered = false;
  bool paused = false;

  RoundEnded? lastRoundResult;
  ScoreUpdate? scoreUpdate;
  ScoreUpdate? leaderboard;
  bool showLeaderboard = false;
  ScoreUpdate? finalScores;

  BonusStakeOptions? bonusStakeOptions;
  int? bonusPalierSelectionne;
  bool bonusStakeEnvoyee = false;

  BonusQuestionStarted? bonusQuestion;
  bool bonusAnswered = false;

  BonusResult? lastBonusResult;

  /// Durée maximale de la tentative de reconnexion automatique au démarrage — au-delà, on
  /// abandonne et on affiche l'écran de connexion manuelle plutôt que de laisser l'écran de
  /// chargement tourner indéfiniment (serveur éteint, Pi pas encore démarré, mauvais réseau...).
  static const _delaiReconnexionAuto = Duration(seconds: 4);

  Future<void> init() async {
    _prefs = await SharedPreferences.getInstance();

    playerId = _prefs!.getString(_prefsPlayerId);
    if (playerId == null) {
      playerId = _genererPlayerId();
      await _prefs!.setString(_prefsPlayerId, playerId!);
    }

    serverUrl = _prefs!.getString(_prefsServerUrl);
    nom = _prefs!.getString(_prefsNom);

    // Tentative silencieuse avec la dernière adresse connue pendant l'écran de chargement — si
    // elle échoue ou traîne trop longtemps, on retombe sur l'écran de connexion manuelle avec
    // l'adresse déjà pré-remplie (voir ConnectScreen), sans message d'erreur alarmant puisque
    // rien n'a encore été tenté explicitement par le joueur.
    if (serverUrl != null) {
      await connect(serverUrl!, timeout: _delaiReconnexionAuto, silent: true);
    } else {
      screen = AppScreen.connect;
      notifyListeners();
    }
  }

  /// Identifiant stable persisté localement — jamais le connectionId SignalR, qui
  /// change à chaque reconnexion. C'est ce qui permet à `JoinGame` de reconnecter
  /// le joueur sans perdre son score après une coupure réseau.
  String _genererPlayerId() {
    final rand = Random.secure();
    final bytes = List<int>.generate(16, (_) => rand.nextInt(256));
    return bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  }

  void setLocalError(String message) {
    errorMessage = message;
    notifyListeners();
  }

  /// À utiliser quand JoinScreen est déjà à l'écran (scan lancé depuis là, déjà connecté au même
  /// serveur — voir QrScanScreen) : [connect] n'étant pas rappelé dans ce cas, il faut notifier
  /// explicitement pour que l'écran déjà monté récupère le nouveau code.
  void setPendingJoinCode(String code) {
    pendingJoinCode = code;
    notifyListeners();
  }

  /// [timeout] borne la tentative (utilisé pour la reconnexion auto au démarrage — voir [init])
  /// ; sans borne pour une connexion manuelle depuis [ConnectScreen], où l'utilisateur voit le
  /// spinner et peut patienter. [silent] masque le message d'erreur en cas d'échec, pour ne pas
  /// afficher une alerte au joueur lors d'une tentative qu'il n'a pas déclenchée lui-même.
  Future<bool> connect(String url, {Duration? timeout, bool silent = false}) async {
    connecting = true;
    errorMessage = null;
    notifyListeners();

    final cleanUrl = url.trim().replaceAll(RegExp(r'/+$'), '');

    // Referme une éventuelle connexion précédente avant d'en ouvrir une nouvelle (ex. scan d'un QR
    // depuis JoinScreen alors qu'on était déjà connecté) — sinon l'ancien HubConnection reste actif
    // en arrière-plan, écoutant toujours ses handlers, sans jamais être arrêté.
    await _hub?.stop();

    _hub = HubConnectionBuilder().withUrl('$cleanUrl/hubs/game').withAutomaticReconnect().build();

    _registerHandlers();

    try {
      final demarrage = _hub!.start()!;
      await (timeout == null ? demarrage : demarrage.timeout(timeout));
      connected = true;
      connecting = false;
      serverUrl = cleanUrl;
      await _prefs?.setString(_prefsServerUrl, cleanUrl);
      screen = AppScreen.join;
      notifyListeners();
      return true;
    } catch (e) {
      connecting = false;
      connected = false;
      screen = AppScreen.connect;
      if (!silent) {
        errorMessage = "Connexion impossible : vérifiez l'adresse et que le serveur tourne.";
      }
      notifyListeners();
      return false;
    }
  }

  void _registerHandlers() {
    final hub = _hub!;

    hub.onclose(({error}) {
      connected = false;
      notifyListeners();
    });

    hub.onreconnecting(({error}) {
      connected = false;
      notifyListeners();
    });

    hub.onreconnected(({connectionId}) async {
      connected = true;
      // Un nouveau connectionId a été émis par le serveur au reconnect : il faut
      // rejouer JoinGame avec le playerId stable pour que le serveur réassocie ce
      // joueur existant plutôt que d'en créer un nouveau (voir GameHub.JoinGame).
      if (gameCode != null && nom != null) {
        try {
          await hub.invoke('JoinGame', args: [gameCode!, nom!, playerId!]);
        } catch (_) {
          // best effort — le joueur reste visible côté UI, il pourra retenter manuellement.
        }
      }
      notifyListeners();
    });

    hub.on('PlayerJoined', (args) {
      final data = args![0] as Map<String, dynamic>;
      final id = data['playerId'] as String;
      if (players.any((p) => p.playerId == id)) return;
      players.add(PlayerInfo(playerId: id, nom: data['nom'] as String));
      notifyListeners();
    });

    hub.on('PlayerReconnected', (args) {
      final data = args![0] as Map<String, dynamic>;
      _updatePlayerConnection(data['playerId'] as String, true);
    });

    hub.on('PlayerDisconnected', (args) {
      final data = args![0] as Map<String, dynamic>;
      _updatePlayerConnection(data['playerId'] as String, false);
    });

    hub.on('PlayerTeamChanged', (args) {
      final data = args![0] as Map<String, dynamic>;
      final changedPlayerId = data['playerId'] as String;
      final newTeamId = data['teamId'] as String;

      if (changedPlayerId == playerId) teamId = newTeamId;
      for (final p in players) {
        if (p.playerId == changedPlayerId) p.teamId = newTeamId;
      }
      notifyListeners();
    });

    // Retour utilisateur (playtest 2026-08-24) : écran déjà présent côté host/écran public avant
    // chaque série, jamais diffusé aux joueurs. Reste affiché jusqu'au prochain écran pertinent
    // (RoundStarted/BonusStakeOptions plus bas) plutôt qu'un minuteur local — voir SerieIntroScreen.
    hub.on('SerieAnnoncee', (args) {
      final data = args![0] as Map<String, dynamic>;
      serieIntro = SerieAnnoncee.fromJson(data);
      screen = AppScreen.serieIntro;
      notifyListeners();
    });

    hub.on('RoundStarted', (args) {
      final data = args![0] as Map<String, dynamic>;
      currentRound = RoundStarted.fromJson(data);
      roundAnswered = false;
      lastRoundResult = null;
      errorMessage = null;
      screen = AppScreen.round;
      notifyListeners();
    });

    hub.on('ScoreUpdate', (args) {
      final data = args![0] as Map<String, dynamic>;
      scoreUpdate = ScoreUpdate.fromJson(data);
      _syncOwnScore();
      notifyListeners();
    });

    hub.on('RoundEnded', (args) {
      final data = args![0] as Map<String, dynamic>;
      lastRoundResult = RoundEnded.fromJson(data);
      screen = AppScreen.roundEnded;
      notifyListeners();
    });

    hub.on('GamePaused', (_) {
      paused = true;
      notifyListeners();
    });

    hub.on('GameResumed', (_) {
      paused = false;
      notifyListeners();
    });

    hub.on('LeaderboardShown', (args) {
      final data = args![0] as Map<String, dynamic>;
      leaderboard = ScoreUpdate.fromJson(data);
      showLeaderboard = true;
      notifyListeners();
    });

    hub.on('GameEnded', (args) {
      final data = args![0] as Map<String, dynamic>;
      finalScores = ScoreUpdate.fromJson(data);
      screen = AppScreen.ended;
      notifyListeners();
    });

    // Déclenché par le host (RejouerPartie côté serveur) — même code, mêmes joueurs,
    // scores remis à zéro côté serveur. Pas de bouton côté joueur : seul le host décide.
    hub.on('GameRestarted', (_) {
      score = 0;
      serieIntro = null;
      currentRound = null;
      roundAnswered = false;
      lastRoundResult = null;
      scoreUpdate = null;
      finalScores = null;
      bonusStakeOptions = null;
      bonusPalierSelectionne = null;
      bonusStakeEnvoyee = false;
      bonusQuestion = null;
      bonusAnswered = false;
      lastBonusResult = null;
      screen = AppScreen.lobby;
      notifyListeners();
    });

    hub.on('BonusStakeOptions', (args) {
      final data = args![0] as Map<String, dynamic>;
      bonusStakeOptions = BonusStakeOptions.fromJson(data);
      bonusPalierSelectionne = null;
      bonusStakeEnvoyee = false;
      screen = AppScreen.bonusStake;
      notifyListeners();
    });

    hub.on('BonusQuestionStarted', (args) => onBonusQuestionStarted(args![0] as Map<String, dynamic>));

    hub.on('BonusResult', (args) => onBonusResult(args![0] as Map<String, dynamic>));
  }

  // Extraits de _registerHandlers en méthodes nommées (visibilité fichier, pas privées) pour être
  // exercables directement par des tests unitaires sans connexion SignalR réelle — voir
  // app/test/services/game_connection_test.dart et docs/refactor-decisions.md section 4.
  void onBonusQuestionStarted(Map<String, dynamic> data) {
    bonusQuestion = BonusQuestionStarted.fromJson(data);
    bonusAnswered = false;
    _introCourseTimer?.cancel();

    if (bonusQuestion!.estCourse) {
      screen = AppScreen.bonusCourseIntro;
      _introCourseTimer = Timer(_dureeIntroCourse, () {
        // Ne bascule que si rien d'autre n'a fait avancer l'état entre-temps (BonusResult peut
        // arriver très vite en mode course si un autre joueur a déjà répondu) — sinon on
        // écraserait un écran plus récent avec l'ancien.
        if (screen == AppScreen.bonusCourseIntro) {
          screen = AppScreen.bonusQuestion;
          notifyListeners();
        }
      });
    } else {
      screen = AppScreen.bonusQuestion;
    }
    notifyListeners();
  }

  void onBonusResult(Map<String, dynamic> data) {
    _introCourseTimer?.cancel();
    lastBonusResult = BonusResult.fromJson(data);
    screen = AppScreen.bonusResult;
    notifyListeners();
  }

  void _updatePlayerConnection(String id, bool estConnecte) {
    for (final p in players) {
      if (p.playerId == id) {
        p.estConnecte = estConnecte;
        break;
      }
    }
    notifyListeners();
  }

  void _syncOwnScore() {
    final mine = scoreUpdate?.joueurs.where((j) => j.playerId == playerId);
    if (mine != null && mine.isNotEmpty) {
      score = mine.first.score;
      teamId = mine.first.teamId;
    }
  }

  void closeLeaderboard() {
    showLeaderboard = false;
    notifyListeners();
  }

  Future<bool> joinGame(String code, String pseudo) async {
    errorMessage = null;
    notifyListeners();

    try {
      final result = await _hub!.invoke('JoinGame', args: [code, pseudo, playerId!]);
      final joinResult = JoinResult.fromJson(result as Map<String, dynamic>);

      if (!joinResult.success) {
        errorMessage = joinResult.errorMessage ?? 'Impossible de rejoindre la partie.';
        notifyListeners();
        return false;
      }

      gameCode = code;
      nom = pseudo;
      score = joinResult.score;
      teamId = joinResult.teamId;
      teams = joinResult.teams;
      await _prefs?.setString(_prefsNom, pseudo);

      // Le serveur ne diffuse PlayerJoined qu'aux AUTRES joueurs déjà présents (GameHub.JoinGame)
      // — sans le roster complet renvoyé ici, un joueur ne voyait ni lui-même (retour
      // utilisateur), ni ceux ayant rejoint avant lui.
      players
        ..clear()
        ..addAll(joinResult.joueurs.map(
          (j) => PlayerInfo(playerId: j.playerId, nom: j.nom, estConnecte: j.estConnecte, teamId: j.teamId),
        ));
      screen = AppScreen.lobby;
      notifyListeners();
      return true;
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
      return false;
    }
  }

  /// Autorisé à tout moment (pas seulement au lobby) — l'état local est mis à jour via
  /// l'événement PlayerTeamChanged plutôt qu'ici, pour rester cohérent avec ce que voient
  /// les autres joueurs et le host.
  Future<void> joinTeam(String teamId) async {
    try {
      await _hub!.invoke('JoinTeam', args: [teamId]);
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  /// Un seul essai par round — déjà appliqué côté serveur (voir RoundService.SoumettreReponse),
  /// mais l'UI doit refléter l'état immédiatement pour ne pas laisser croire qu'une
  /// deuxième soumission est possible.
  Future<void> submitAnswer(String reponse) async {
    if (roundAnswered || paused) return;

    roundAnswered = true;
    notifyListeners();

    try {
      await _hub?.invoke('SubmitAnswer', args: [
        {'reponse': reponse}
      ]);
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  /// Choix du palier de mise, à l'aveugle avant de découvrir la question — un seul essai
  /// par round bonus, déjà appliqué côté serveur (voir BonusRoundService.EnregistrerMise).
  Future<void> selectStake(int palierIndex) async {
    if (bonusStakeEnvoyee || paused) return;

    bonusPalierSelectionne = palierIndex;
    bonusStakeEnvoyee = true;
    notifyListeners();

    try {
      await _hub?.invoke('SelectStake', args: [
        {'palierIndex': palierIndex}
      ]);
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  Future<void> submitBonusAnswer(String reponse) async {
    if (bonusAnswered || paused) return;

    bonusAnswered = true;
    notifyListeners();

    try {
      await _hub?.invoke('SubmitBonusAnswer', args: [
        {'reponse': reponse}
      ]);
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  @override
  void dispose() {
    _introCourseTimer?.cancel();
    _hub?.stop();
    super.dispose();
  }
}
