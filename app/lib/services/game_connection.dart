import 'dart:async';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:signalr_netcore/signalr_client.dart';

import 'mdns_resolver.dart';

import '../models/bonus_question_started.dart';
import '../models/bonus_result.dart';
import '../models/bonus_stake_options.dart';
import '../models/etat_courant_connexion.dart';
import '../models/etat_courant_joueur.dart';
import '../models/join_result.dart';
import '../models/morceau_joue.dart';
import '../models/raison_signalement.dart';
import '../models/round_ended.dart';
import '../models/round_started.dart';
import '../models/score_update.dart';
import '../models/serie_annoncee.dart';
import '../models/team.dart';
import '../models/titre.dart';
import 'update_checker.dart';

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
const _prefsGameCode = 'blindify_game_code';

/// Pré-remplissage du champ adresse serveur tant qu'aucune connexion n'a encore été
/// enregistrée (voir docs/architecture.md "Nom local au lieu de l'IP" — hostname `pi`
/// configuré sur le Pi via raspi-config) — évite de laisser une IP de poste de dev comme
/// première suggestion vue par un joueur.
const defaultServerUrl = 'http://pi.local:5000';

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
  Timer? _reconnectTimer;
  Timer? _rejoinRetryTimer;

  String? playerId;
  String? serverUrl;
  String? nom;
  String? gameCode;

  /// Adresse effectivement joignable pour les requêtes réseau (hub SignalR, images) —
  /// identique à [serverUrl] sauf quand celui-ci est un hostname `.local` résolu en IP via
  /// mDNS (voir [resolveMdnsHost]) : [serverUrl] garde alors la forme lisible saisie/persistée,
  /// celle-ci la forme réellement utilisable sur Android.
  String? _resolvedUrl;

  /// Code de partie extrait d'un QR scanné (voir QrScanScreen), en attente d'être consommé par
  /// JoinScreen pour pré-remplir son champ — remis à null après lecture pour ne pas re-préremplir
  /// un futur passage sur cet écran (ex. après une partie terminée, code manuel suivant).
  String? pendingJoinCode;

  /// URL complète d'une pochette (servie sous /files, comme l'audio côté host — voir
  /// Program.cs). null si le morceau n'a pas de coverPath.
  String? coverUrl(String? coverPath) =>
      coverPath == null ? null : '${_resolvedUrl ?? serverUrl}/files/$coverPath';

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
  List<Titre> finalTitres = [];

  BonusStakeOptions? bonusStakeOptions;
  int? bonusPalierSelectionne;
  bool bonusStakeEnvoyee = false;

  BonusQuestionStarted? bonusQuestion;
  bool bonusAnswered = false;

  BonusResult? lastBonusResult;

  /// true une fois authentifié via [authenticateAdmin] — débloque les actions admin (pause/tableau
  /// général/fin de partie) dans les réglages, EN PLUS du host web, sans jamais le déloger (voir
  /// GameHub.AuthenticateAdmin/ResoudreSessionHostOuAdmin côté serveur). Redevient false à chaque
  /// nouvelle connexion (pas persisté) : le mot de passe reste à ressaisir, comme le hostSecret côté
  /// host web n'est jamais mémorisé en clair côté client.
  bool isAdmin = false;
  String? adminError;

  /// Morceaux joués ET révélés dans la partie courante (V2, section 12.4) — alimenté depuis RoundEnded/
  /// BonusResult, jamais avant le reveal (voir MorceauJoue). Vidé à GameRestarted (nouvelle manche).
  final List<MorceauJoue> morceauxJoues = [];
  String? flagError;

  /// Mise à jour APK détectée sur le serveur (voir services/update_checker.dart) — Android natif
  /// uniquement (web/iOS n'ont pas cette notion d'APK installé, voir apk_update.dart). Bannière
  /// visible dès le lancement (retour utilisateur), fermable pour la session courante uniquement :
  /// [updateBannerFermee] repart à false à chaque nouvelle connexion, pour ne jamais laisser un
  /// joueur passer une session entière sans savoir qu'une mise à jour existe.
  bool updateDisponible = false;
  String? updateVersionDistante;
  bool updateBannerFermee = false;

  void fermerBanniereMiseAJour() {
    updateBannerFermee = true;
    notifyListeners();
  }

  /// Volontairement non-bloquant (pas de await côté appelant) : ne doit jamais retarder la
  /// connexion ni la rejointe automatique de partie pour une vérification annexe. Android natif
  /// uniquement (web/iOS n'ont pas cette notion d'APK installé, voir apk_update.dart). Best-effort
  /// côté update_checker.dart (jamais d'exception propagée jusqu'ici).
  void _verifierMiseAJourEnArrierePlan(String resolvedUrl) {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) return;
    unawaited(() async {
      final resultat = await verifierMiseAJourDisponible(resolvedUrl);
      updateDisponible = resultat.disponible;
      updateVersionDistante = resultat.versionDistante;
      if (resultat.disponible) updateBannerFermee = false;
      notifyListeners();
    }());
  }

  /// Durée maximale de la tentative de reconnexion automatique au démarrage — au-delà, on
  /// abandonne et on affiche l'écran de connexion manuelle plutôt que de laisser l'écran de
  /// chargement tourner indéfiniment (serveur éteint, Pi pas encore démarré, mauvais réseau...).
  static const _delaiReconnexionAuto = Duration(seconds: 4);

  /// Intervalle de nouvelle tentative une fois que le reconnect auto de SignalR
  /// (withAutomaticReconnect, fenêtre de retry bornée) a abandonné et fermé la connexion — voir
  /// [_planifierReconnexion].
  static const _delaiRetryReconnexion = Duration(seconds: 6);

  Future<void> init() async {
    _prefs = await SharedPreferences.getInstance();

    playerId = _prefs!.getString(_prefsPlayerId);
    if (playerId == null) {
      playerId = _genererPlayerId();
      await _prefs!.setString(_prefsPlayerId, playerId!);
    }

    serverUrl = _prefs!.getString(_prefsServerUrl);
    nom = _prefs!.getString(_prefsNom);
    // Code de la partie en cours, persisté par joinGame — retour utilisateur (playtest
    // 2026-09-06) : un joueur déconnecté (freeze/coupure réseau) n'avait aucun moyen de retrouver
    // le code pour se reconnecter, seulement affiché au lobby avant le début de la partie. Avec
    // ce code + le playerId stable déjà persisté, [connect] retente automatiquement de rejoindre
    // la même partie ci-dessous, sans ressaisie.
    gameCode = _prefs!.getString(_prefsGameCode);

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
    // Sur Android, un hostname .local (mDNS) n'est pas résolu au niveau socket — remplacé ici par
    // son IP concrète avant toute requête ; inchangé pour un autre hôte ou si la résolution échoue
    // (voir resolveMdnsHost, docs/architecture.md "Nom local au lieu de l'IP").
    final resolvedUrl = await resolveMdnsHost(cleanUrl);

    // Referme une éventuelle connexion précédente avant d'en ouvrir une nouvelle (ex. scan d'un QR
    // depuis JoinScreen alors qu'on était déjà connecté) — sinon l'ancien HubConnection reste actif
    // en arrière-plan, écoutant toujours ses handlers, sans jamais être arrêté.
    await _hub?.stop();

    // V2 — reconnexion automatique : dès que le code de partie est connu (pas au tout premier
    // lancement, avant tout JoinGame), l'URL du hub porte ?code&playerId. SignalR réutilise cette
    // même URL à chaque reconnexion transport (withAutomaticReconnect) : GameHub.OnConnectedAsync
    // peut alors rattacher automatiquement ce joueur (score, phase en cours) sans attendre le
    // rappel explicite à JoinGame fait plus bas par le handler onreconnected (qui reste en place
    // comme filet de sécurité si cette voie plus rapide échoue pour une raison quelconque).
    final hubUrl = gameCode != null && playerId != null
        ? '$resolvedUrl/hubs/game?code=$gameCode&playerId=$playerId'
        : '$resolvedUrl/hubs/game';
    _hub = HubConnectionBuilder().withUrl(hubUrl).withAutomaticReconnect().build();

    _registerHandlers();

    try {
      final demarrage = _hub!.start()!;
      await (timeout == null ? demarrage : demarrage.timeout(timeout));
      connected = true;
      connecting = false;
      serverUrl = cleanUrl;
      _verifierMiseAJourEnArrierePlan(resolvedUrl);
      _resolvedUrl = resolvedUrl;
      await _prefs?.setString(_prefsServerUrl, cleanUrl);

      // Rejoindre automatiquement la partie en cours si on en connaît une (redémarrage de l'appli
      // après un freeze, reconnexion réseau) — sauf si un code vient d'être scanné via QR
      // (pendingJoinCode) : dans ce cas l'utilisateur vise explicitement une AUTRE partie, ne pas
      // écraser son intention avec l'ancien gameCode. Le serveur réassocie via playerId
      // (GameHub.JoinGame), donc aucune perte de score.
      if (pendingJoinCode == null && gameCode != null && nom != null) {
        final rejoint = await joinGame(gameCode!, nom!);
        if (!rejoint) {
          // Code stocké devenu invalide (partie terminée entre-temps, etc.) — ne pas rester
          // bloqué : repli sur l'écran de connexion manuelle plutôt que de retenter en boucle.
          await _prefs?.remove(_prefsGameCode);
          gameCode = null;
          errorMessage = null;
          screen = AppScreen.join;
          notifyListeners();
        }
      } else {
        screen = AppScreen.join;
        notifyListeners();
      }
      return true;
    } catch (e) {
      connecting = false;
      connected = false;
      // Ne yank pas vers l'écran de connexion manuelle si une partie est en cours (gameCode connu)
      // : ça arriverait à chaque tentative silencieuse ratée de [_planifierReconnexion] pendant une
      // coupure réseau prolongée, alors que l'écran courant (round, lobby...) doit rester visible
      // en attendant que la connexion revienne.
      if (gameCode == null) screen = AppScreen.connect;
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
      _planifierReconnexion();
    });

    hub.onreconnecting(({error}) {
      connected = false;
      notifyListeners();
    });

    hub.onreconnected(({connectionId}) async {
      connected = true;
      notifyListeners();
      // Un nouveau connectionId a été émis par le serveur au reconnect : il faut rejouer JoinGame
      // avec le playerId stable pour que le serveur réassocie ce joueur existant (score, équipe)
      // ET le rajoute au groupe SignalR de la partie (voir GameHub.JoinGame, Groups.AddToGroupAsync)
      // — sans ça, il reste connecté au hub mais ne recevrait plus aucune diffusion (RoundStarted
      // compris) jusqu'à la fin de la partie. Retour utilisateur (playtest 2026-09-06) : l'ancien
      // code traitait cet appel en "best effort" et abandonnait silencieusement en cas d'échec —
      // ici on retente jusqu'à ce que ça marche, pour garantir que le joueur peut au moins
      // participer au prochain round plutôt que de rester orphelin du groupe sans le savoir.
      if (gameCode != null && nom != null) {
        final rejoint = await joinGame(gameCode!, nom!, actualiserEcran: false);
        if (!rejoint) _planifierRejoinApresReconnexionCourte();
      }
    });

    // V2 : envoyé par GameHub.OnConnectedAsync quand cette connexion (URL avec ?code&playerId, voir
    // connect() ci-dessus) vient d'être automatiquement rattachée à ce joueur — arrive typiquement
    // plus tôt que le rejoint via onreconnected ci-dessus (pas d'aller-retour Invoke supplémentaire),
    // donc appliqué dès réception. actualiserEcran=false : mêmes raisons que onreconnected, ne pas
    // écraser silencieusement l'écran courant pour un aléa réseau s'il n'y a rien de plus récent.
    hub.on('EtatCourant', (args) {
      final data = args![0] as Map<String, dynamic>;
      final etat = EtatCourantConnexion.fromJson(data);
      score = etat.score;
      teamId = etat.teamId;
      appliquerEtatCourant(etat.etatCourant, actualiserEcran: false);
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
      _ajouterMorceauJoue(lastRoundResult!.trackId, lastRoundResult!.title, lastRoundResult!.artist);
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
      finalScores = ScoreUpdate.fromJson(data['score'] as Map<String, dynamic>);
      finalTitres = (data['titres'] as List<dynamic>)
          .map((e) => Titre.fromJson(e as Map<String, dynamic>))
          .toList();
      screen = AppScreen.ended;
      // On ne vide QUE le pref persisté (lu au prochain démarrage froid de l'appli, cf. connect())
      // pour ne pas retenter un rejoin auto sur une partie terminée — gameCode en mémoire, lui,
      // reste renseigné : le host peut relancer la même partie (RejouerPartie, même code, voir
      // handler GameRestarted juste en dessous), et l'écran de salon en a besoin pour réafficher
      // le code. Bug retour utilisateur (2026-09-13) : gameCode remis à null ici faisait
      // apparaître le code vide sur l'écran de salon après un "rejouer" côté host.
      _prefs?.remove(_prefsGameCode);
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
      finalTitres = [];
      morceauxJoues.clear();
      flagError = null;
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
    _ajouterMorceauJoue(lastBonusResult!.trackId, lastBonusResult!.title, lastBonusResult!.artist);
    screen = AppScreen.bonusResult;
    notifyListeners();
  }

  void _ajouterMorceauJoue(String trackId, String titre, String artiste) {
    if (morceauxJoues.any((m) => m.trackId == trackId)) return;
    morceauxJoues.add(MorceauJoue(trackId: trackId, titre: titre, artiste: artiste));
  }

  /// Le reconnect auto de SignalR (withAutomaticReconnect) a une fenêtre de retry bornée ; une fois
  /// épuisée, il déclenche onclose et n'insiste plus. Sans repli ici, une coupure réseau prolongée
  /// laissait le joueur bloqué sur "déconnecté" sans aucune action possible (retour utilisateur,
  /// playtest 2026-09-06). On retente nous-mêmes à intervalle régulier, indéfiniment, jusqu'à
  /// reconnexion ou fermeture de l'appli.
  void _planifierReconnexion() {
    if (_reconnectTimer != null || serverUrl == null) return;
    _reconnectTimer = Timer.periodic(_delaiRetryReconnexion, (_) async {
      if (connected) {
        _reconnectTimer?.cancel();
        _reconnectTimer = null;
        return;
      }
      final ok = await connect(serverUrl!, timeout: _delaiReconnexionAuto, silent: true);
      if (ok) {
        _reconnectTimer?.cancel();
        _reconnectTimer = null;
      }
    });
  }

  /// Nombre de tentatives avant d'abandonner (~15s à 3s d'intervalle) — au-delà, le salon n'existe
  /// probablement plus (JoinGame répond "Partie introuvable" en boucle) plutôt qu'un simple aléa
  /// réseau ponctuel, qui se résorberait en 1-2 tentatives.
  static const _maxTentativesRejoinCourt = 5;

  /// Retente l'appel JoinGame après une reconnexion transport réussie (onreconnected) dont
  /// l'invoke a échoué — le hub est bien vivant (sinon onclose se serait déclenché à la place et
  /// [_planifierReconnexion] aurait pris le relais), seul le rattachement au groupe de la partie a
  /// échoué. Retente à intervalle court tant que la connexion transport tient.
  ///
  /// Retour utilisateur : borné plutôt qu'indéfini — si le salon a disparu pendant la coupure (host
  /// qui a redémarré le backend, partie terminée entre-temps : l'état est 100% en mémoire, jamais
  /// persisté), l'ancien code retentait pour toujours en silence (actualiserEcran=false) sans
  /// jamais prévenir le joueur ni le laisser agir — bloqué indéfiniment sur un écran de jeu périmé
  /// alors que le transport SignalR restait, lui, bien connecté. Une fois la limite atteinte, le
  /// joueur est déconnecté du salon par défaut (gameCode oublié, repli sur l'écran de connexion à
  /// une partie) plutôt que de laisser croire que tout va bien.
  void _planifierRejoinApresReconnexionCourte() {
    if (_rejoinRetryTimer != null) return;
    var tentatives = 0;
    _rejoinRetryTimer = Timer.periodic(const Duration(seconds: 3), (timer) async {
      if (!connected || gameCode == null || nom == null) {
        timer.cancel();
        _rejoinRetryTimer = null;
        return;
      }

      tentatives++;
      final ok = await joinGame(gameCode!, nom!, actualiserEcran: false);
      if (ok) {
        timer.cancel();
        _rejoinRetryTimer = null;
        return;
      }

      if (tentatives >= _maxTentativesRejoinCourt) {
        timer.cancel();
        _rejoinRetryTimer = null;
        await _prefs?.remove(_prefsGameCode);
        gameCode = null;
        errorMessage = "Impossible de retrouver la partie — le salon n'existe peut-être plus.";
        screen = AppScreen.join;
        notifyListeners();
      }
    });
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

  /// [actualiserEcran] ne contrôle QUE le repli sur le lobby quand rien n'est activement en cours
  /// (etatCourant == null côté serveur) et l'affichage de [errorMessage] en cas d'échec — vrai pour
  /// un join explicite (JoinScreen) ou un rejoin après redémarrage de l'appli/coupure prolongée
  /// (aucun écran de jeu valide localement dans ces cas). Faux pour une simple réassociation en
  /// arrière-plan après une reconnexion transport courte (onreconnected,
  /// [_planifierRejoinApresReconnexionCourte]) : pas la peine d'écraser silencieusement l'écran du
  /// joueur pour un aléa réseau de quelques secondes s'il n'y a rien de plus récent à afficher.
  /// Quand le serveur renvoie un etatCourant (round classique ou question bonus actif), il est
  /// toujours appliqué, y compris avec actualiserEcran=false : la phase réelle a pu changer pendant
  /// la coupure (round suivant démarré entre-temps) — voir [appliquerEtatCourant], retour
  /// utilisateur (playtest 2026-09-06, "pouvoir rejoindre au moins le prochain round").
  Future<bool> joinGame(String code, String pseudo, {bool actualiserEcran = true}) async {
    errorMessage = null;
    if (actualiserEcran) notifyListeners();

    try {
      final result = await _hub!.invoke('JoinGame', args: [code, pseudo, playerId!]);
      final joinResult = JoinResult.fromJson(result as Map<String, dynamic>);

      if (!joinResult.success) {
        if (actualiserEcran) {
          errorMessage = joinResult.errorMessage ?? 'Impossible de rejoindre la partie.';
          notifyListeners();
        }
        return false;
      }

      gameCode = code;
      nom = pseudo;
      score = joinResult.score;
      teamId = joinResult.teamId;
      teams = joinResult.teams;
      await _prefs?.setString(_prefsNom, pseudo);
      await _prefs?.setString(_prefsGameCode, code);

      // Le serveur ne diffuse PlayerJoined qu'aux AUTRES joueurs déjà présents (GameHub.JoinGame)
      // — sans le roster complet renvoyé ici, un joueur ne voyait ni lui-même (retour
      // utilisateur), ni ceux ayant rejoint avant lui.
      players
        ..clear()
        ..addAll(joinResult.joueurs.map(
          (j) => PlayerInfo(playerId: j.playerId, nom: j.nom, estConnecte: j.estConnecte, teamId: j.teamId),
        ));
      appliquerEtatCourant(joinResult.etatCourant, actualiserEcran: actualiserEcran);
      notifyListeners();
      return true;
    } catch (e) {
      if (actualiserEcran) {
        errorMessage = 'Erreur : ${e.toString()}';
        notifyListeners();
      }
      return false;
    }
  }

  /// Rebranche l'état local sur la phase renvoyée par le serveur (round classique ou question
  /// bonus activement en cours) — appliqué dès que [etat] est non null, y compris avec
  /// actualiserEcran=false (réassociation en arrière-plan) : la phase réelle a pu changer pendant
  /// la coupure (round suivant démarré entre-temps), autant se resynchroniser tout de suite plutôt
  /// que d'attendre le prochain événement serveur. [actualiserEcran] ne contrôle que le repli sur
  /// le lobby quand rien n'est actif (voir [joinGame]).
  void appliquerEtatCourant(EtatCourantJoueur? etat, {required bool actualiserEcran}) {
    if (etat == null) {
      if (actualiserEcran) screen = AppScreen.lobby;
      return;
    }

    paused = etat.enPause;

    switch (etat.phase) {
      case PhaseJoueur.roundClassique:
        currentRound = etat.round;
        roundAnswered = etat.dejaRepondu;
        lastRoundResult = null;
        screen = AppScreen.round;
      case PhaseJoueur.bonusMise:
        bonusStakeOptions = etat.bonusMise;
        bonusStakeEnvoyee = etat.dejaRepondu;
        screen = AppScreen.bonusStake;
      case PhaseJoueur.bonusQuestion:
        bonusQuestion = etat.bonusQuestion;
        bonusAnswered = etat.dejaRepondu;
        screen = AppScreen.bonusQuestion;
      case PhaseJoueur.aucune:
        // Jamais renvoyé par le serveur en pratique (GameHub.ConstruireEtatCourantJoueur renvoie
        // null plutôt qu'un DTO à Phase.Aucune) — présent pour l'exhaustivité du switch.
        if (actualiserEcran) screen = AppScreen.lobby;
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

  /// Mot de passe distinct du hostSecret de la page web (voir GameHub.AuthenticateAdmin) — pensé
  /// pour être saisi une fois dans les réglages plutôt que d'exiger un accès physique au host pour
  /// pause/tableau général/fin de partie. Nécessite d'avoir déjà rejoint une partie (JoinGame) :
  /// le serveur résout la session via la connexion courante, pas via un code fourni ici.
  Future<bool> authenticateAdmin(String password) async {
    adminError = null;
    try {
      final result = await _hub!.invoke('AuthenticateAdmin', args: [password]);
      final data = result as Map<String, dynamic>;
      final success = data['success'] as bool;
      isAdmin = success;
      if (!success) adminError = data['errorMessage'] as String?;
      notifyListeners();
      return success;
    } catch (e) {
      adminError = 'Erreur : ${e.toString()}';
      notifyListeners();
      return false;
    }
  }

  /// Les 4 méthodes ci-dessous ne mettent à jour aucun état local : le serveur diffuse
  /// GamePaused/GameResumed/LeaderboardShown/GameEnded à tout le groupe (host web compris), déjà
  /// géré par les handlers ci-dessus — y compris pour CE téléphone, qui les reçoit comme n'importe
  /// quel autre client du groupe.
  Future<void> adminPauseGame() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('PauseGame');
    } catch (e) {
      adminError = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  Future<void> adminResumeGame() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('ResumeGame');
    } catch (e) {
      adminError = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  Future<void> adminShowLeaderboard() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('ShowLeaderboard');
    } catch (e) {
      adminError = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  Future<void> adminEndGame() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('EndGame');
    } catch (e) {
      adminError = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  /// V2, section 12.4 — voir GameHub.SignalerMorceau. `trackId` doit venir de [morceauxJoues] (déjà
  /// révélé dans cette partie), jamais saisi librement : le serveur rejette sinon avec une HubException.
  Future<bool> signalerMorceau(String trackId, RaisonSignalement raison, String? commentaire) async {
    if (!isAdmin) return false;
    flagError = null;
    try {
      await _hub!.invoke('SignalerMorceau', args: [
        {
          'trackId': trackId,
          'raison': raison.code,
          if (commentaire != null && commentaire.trim().isNotEmpty) 'commentaire': commentaire.trim(),
        }
      ]);
      return true;
    } catch (e) {
      flagError = 'Erreur : ${e.toString()}';
      notifyListeners();
      return false;
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
      // roundId omis si currentRound est encore null (ne devrait pas arriver en usage réel — cet
      // écran n'est affiché qu'après réception de RoundStarted) : le serveur traite alors la
      // soumission comme un roundId périmé et l'ignore silencieusement, plutôt que de planter.
      await _hub?.invoke('SubmitAnswer', args: [
        {if (currentRound != null) 'roundId': currentRound!.roundId, 'reponse': reponse}
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
        {if (bonusStakeOptions != null) 'roundId': bonusStakeOptions!.roundId, 'palierIndex': palierIndex}
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
        {if (bonusQuestion != null) 'roundId': bonusQuestion!.roundId, 'reponse': reponse}
      ]);
    } catch (e) {
      errorMessage = 'Erreur : ${e.toString()}';
      notifyListeners();
    }
  }

  @override
  void dispose() {
    _introCourseTimer?.cancel();
    _reconnectTimer?.cancel();
    _rejoinRetryTimer?.cancel();
    _hub?.stop();
    super.dispose();
  }
}
