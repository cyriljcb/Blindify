import 'dart:async';
import 'dart:math';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:signalr_netcore/signalr_client.dart';

import 'haptique.dart';
import 'decouverte_serveur.dart';
import 'mdns_resolver.dart';

import '../models/bonus_question_started.dart';
import '../models/bonus_result.dart';
import '../models/bonus_stake_options.dart';
import '../models/etat_courant_connexion.dart';
import '../models/etat_courant_joueur.dart';
import '../models/joker_indice.dart';
import '../models/join_result.dart';
import '../models/morceau_joue.dart';
import '../models/raison_signalement.dart';
import '../models/round_ended.dart';
import '../models/round_started.dart';
import '../models/score_update.dart';
import '../models/serie_annoncee.dart';
import '../models/team.dart';
import '../models/titre.dart';
import '../models/vote_themes.dart';
import 'update_checker.dart';

enum AppScreen { loading, connect, join, lobby, serieIntro, round, roundEnded, bonusStake, bonusCourseIntro, bonusQuestion, bonusResult, ended }

class PlayerInfo {
  PlayerInfo({required this.playerId, required this.nom, this.estConnecte = true, this.teamId, this.aQuitte = false});

  final String playerId;
  final String nom;
  bool estConnecte;
  String? teamId;

  /// Parti volontairement en cours de partie (QuitterPartie) : gardé au classement, plus attendu.
  bool aQuitte;
}

const _prefsPlayerId = 'blindify_player_id';
const _prefsServerUrl = 'blindify_server_url';
const _prefsNom = 'blindify_nom';
const _prefsGameCode = 'blindify_game_code';

/// Jeton du joueur pour le salon en cours, sous la forme « CODE:jeton » (audit 2026-11-10) — exigé
/// par le serveur pour reprendre ce playerId (JoinGame, reconnexion automatique).
const _prefsJeton = 'blindify_jeton';

/// Longueur maximale d'un pseudo — même borne que le serveur (GameHub.LongueurMaxNomJoueur).
const longueurMaxNom = 24;

/// Message lisible d'une erreur d'appel au hub : le texte de la HubException côté serveur plutôt
/// que la trace SignalR brute (audit 2026-11-10).
String messageErreur(Object e) {
  final texte = e.toString();
  final marqueur = texte.lastIndexOf('HubException:');
  if (marqueur >= 0) return texte.substring(marqueur + 'HubException:'.length).trim();
  if (texte.contains('Cannot send data') || texte.contains('not in the') || e is StateError) {
    return 'Connexion au serveur indisponible — réessaie dans un instant.';
  }
  return 'Une erreur est survenue — réessaie.';
}

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

  /// Jeton remis par le serveur pour [gameCode] (voir [_prefsJeton]).
  String? _jeton;

  /// Code porté par l'URL du hub en cours (?code=) — si on rejoint un AUTRE salon sur la même
  /// connexion, une reconnexion transport rattacherait l'ancien : la connexion est alors recréée.
  String? _codeDansUrlHub;

  /// La partie du salon courant est terminée (GameEnded, ou join renvoyant Etat=Termine) : le code
  /// n'est plus persisté ni rejoint automatiquement au prochain lancement (audit 2026-11-10 : un
  /// lobby fantôme réapparaissait), jusqu'à GameRestarted ou un join explicite.
  bool partieTerminee = false;

  /// Partie lancée mais aucune phase active (entre deux rounds) au moment du join : le salon
  /// affiche « Partie en cours » au lieu de « Salle d'attente ».
  bool partieEnCours = false;

  /// Instant de réception de la phase chronométrée courante (round, mise ou question bonus) — base
  /// du décompte côté écran (voir AnswerPhaseScreen), indépendante des tics du minuteur local.
  DateTime? phaseRecueA;

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

  /// URL complète de la pochette floutée servie par le joker (V2, section 12.7) — [relativeUrl]
  /// est déjà le chemin renvoyé par le serveur (`/api/joker/cover/{jeton}`), à préfixer par
  /// l'adresse du serveur, contrairement à [coverUrl] qui construit le chemin lui-même.
  String? resolveServerUrl(String? relativeUrl) =>
      relativeUrl == null ? null : '${_resolvedUrl ?? serverUrl}$relativeUrl';

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

  /// Vrai quand le dernier envoi de réponse (round classique ou bonus) a échoué malgré les
  /// reprises de [_invoquerAvecReprise] — l'écran de réponse le signale et rend la main au joueur
  /// pour qu'il réessaie, au lieu d'afficher "Réponse envoyée" alors que le serveur n'a rien reçu.
  bool envoiReponseEchoue = false;

  /// Valeur envoyée pour la question en cours (round classique ou bonus) — l'écran de réponse
  /// garde la tuile correspondante encadrée « Verrouillé » (refonte UI, lot 1). null après une
  /// reconnexion en pleine phase : le serveur ne renvoie pas ce qui avait été choisi.
  String? reponseChoisie;

  /// Joueurs ayant déjà répondu à la question en cours, d'après PlayerAnswered (diffusé à tout le
  /// groupe) — alimente « 5/8 ont répondu ». Sous-estimé après une reconnexion en pleine phase :
  /// les réponses arrivées pendant la coupure ne sont pas rejouées.
  final Set<String> repondants = {};

  /// V2, section 12.7 — un joker par joueur et par partie complète, optimiste par défaut (corrigé
  /// dès JoinGame/EtatCourant/reconnexion). jokerIndiceActuel : indice obtenu pour LE ROUND EN
  /// COURS (soit via [utiliserJoker], soit déjà présent dans currentRound.jokerIndice après une
  /// reconnexion) — remis à null à chaque nouveau round.
  bool jokerDisponible = true;
  JokerIndice? jokerIndiceActuel;

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

  /// Version dont la bannière a été fermée — une vérification ultérieure qui retrouve la même
  /// version ne la réaffiche pas (sinon la vérification périodique la ferait réapparaître en boucle),
  /// mais une version plus récente encore la réaffiche.
  String? _versionBanniereFermee;

  void fermerBanniereMiseAJour() {
    updateBannerFermee = true;
    _versionBanniereFermee = updateVersionDistante;
    notifyListeners();
  }

  /// Adresse à utiliser pour télécharger l'APK — l'IP résolue plutôt que le nom `.local` saisi :
  /// le navigateur Android, comme les sockets de l'app, ne résout pas le mDNS (voir resolveMdnsHost),
  /// le téléchargement échouait donc quand le serveur avait été saisi sous la forme `pi.local`.
  String get urlTelechargementApk => _resolvedUrl ?? serverUrl ?? '';

  static const _intervalleVerificationMiseAJour = Duration(minutes: 3);
  Timer? _verificationMiseAJourTimer;
  AppLifecycleListener? _cycleDeVie;

  /// Retour utilisateur (2026-09-27, "la notification de mise à jour ne fonctionne pas bien") : la
  /// vérification n'avait lieu qu'à [connect], donc une fois par lancement de l'app. Le cas typique
  /// — app déjà ouverte pendant un déploiement, le backend redémarre et SignalR se reconnecte tout
  /// seul (onreconnected, sans repasser par connect) — ne déclenchait jamais la bannière. Vérifie
  /// désormais aussi à chaque reconnexion, au retour au premier plan, et périodiquement.
  void _demarrerVerificationsMiseAJour() {
    _verificationMiseAJourTimer ??= Timer.periodic(_intervalleVerificationMiseAJour, (_) => _verifierMiseAJour());
    _cycleDeVie ??= AppLifecycleListener(onResume: _auRetourAuPremierPlan);
  }

  void _verifierMiseAJour() {
    final url = _resolvedUrl;
    if (url != null) _verifierMiseAJourEnArrierePlan(url);
  }

  /// Volontairement non-bloquant (pas de await côté appelant) : ne doit jamais retarder la
  /// connexion ni la rejointe automatique de partie pour une vérification annexe. Android natif
  /// uniquement (web/iOS n'ont pas cette notion d'APK installé, voir apk_update.dart). Best-effort
  /// côté update_checker.dart (jamais d'exception propagée jusqu'ici).
  void _verifierMiseAJourEnArrierePlan(String resolvedUrl) {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) return;
    _demarrerVerificationsMiseAJour();
    unawaited(() async {
      final resultat = await verifierMiseAJourDisponible(resolvedUrl);
      // Un échec réseau ponctuel ne doit pas masquer une mise à jour déjà détectée.
      if (resultat.echec) return;
      final changement = resultat.disponible != updateDisponible || resultat.versionDistante != updateVersionDistante;
      updateDisponible = resultat.disponible;
      updateVersionDistante = resultat.versionDistante;
      if (resultat.disponible && resultat.versionDistante != _versionBanniereFermee) updateBannerFermee = false;
      if (changement) notifyListeners();
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
    _jeton = _jetonPersistePour(gameCode);

    // Tentative silencieuse avec la dernière adresse connue pendant l'écran de chargement — si
    // elle échoue ou traîne trop longtemps, on retombe sur l'écran de connexion manuelle avec
    // l'adresse déjà pré-remplie (voir ConnectScreen), sans message d'erreur alarmant puisque
    // rien n'a encore été tenté explicitement par le joueur.
    final connecte = serverUrl != null && await connect(serverUrl!, timeout: _delaiReconnexionAuto, silent: true);
    if (!connecte) {
      // Échec de connect() : il a déjà basculé sur l'écran de connexion s'il n'y avait pas de partie
      // en cours (gameCode null) — sinon l'écran courant est volontairement conservé.
      if (serverUrl == null) {
        screen = AppScreen.connect;
        notifyListeners();
      }
      // Premier lancement, ou Pi déplacé/IP changée depuis la dernière partie : recherche
      // automatique sur le réseau local avant de laisser le joueur scanner le QR ou saisir l'adresse.
      final trouve = await chercherEtConnecter(silent: true);
      // Audit 2026-11-10 (critique) : avec une partie mémorisée et un serveur injoignable (Pi qui
      // démarre, Wi-Fi pas encore connecté), l'écran de chargement restait affiché pour toujours,
      // sans aucune nouvelle tentative. On passe à l'écran de connexion — avec un message — et on
      // continue de réessayer en arrière-plan : la partie est rejointe dès que le serveur répond.
      if (!trouve && !connected) {
        if (screen == AppScreen.loading) {
          screen = AppScreen.connect;
          if (gameCode != null) errorMessage = 'Serveur injoignable — nouvelle tentative automatique en cours…';
          notifyListeners();
        }
        _planifierReconnexion();
      }
    }
  }

  /// Bouton « Choisir un autre serveur » de l'écran de chargement : abandonne l'attente de la
  /// reconnexion automatique (qui continue en arrière-plan) et laisse la main au joueur.
  void afficherEcranConnexion() {
    screen = AppScreen.connect;
    notifyListeners();
  }

  String? _jetonPersistePour(String? code) {
    final brut = _prefs?.getString(_prefsJeton);
    if (code == null || brut == null) return null;
    final separateur = brut.indexOf(':');
    return separateur > 0 && brut.substring(0, separateur) == code ? brut.substring(separateur + 1) : null;
  }

  Future<void> _oublierPartie() async {
    await _prefs?.remove(_prefsGameCode);
    await _prefs?.remove(_prefsJeton);
    gameCode = null;
    _jeton = null;
  }

  /// Vrai pendant un scan du réseau local ([chercherEtConnecter]) — l'écran de connexion affiche
  /// alors « Recherche du serveur… » à la place des boutons.
  bool rechercheServeur = false;

  /// Cherche le serveur sur le sous-réseau du téléphone (voir [chercherServeur]) puis s'y connecte.
  /// [silent] : même sens que pour [connect] — pas de message d'erreur pour la recherche lancée
  /// automatiquement au démarrage, seulement pour celle déclenchée par le bouton du joueur.
  Future<bool> chercherEtConnecter({bool silent = false}) async {
    if (rechercheServeur || connecting) return false;
    rechercheServeur = true;
    errorMessage = null;
    notifyListeners();

    final url = await chercherServeur();
    rechercheServeur = false;
    if (url == null) {
      if (!silent) {
        errorMessage = 'Aucun serveur Blindify trouvé sur ce réseau — vérifie que tu es sur le même Wi-Fi que le Pi.';
      }
      notifyListeners();
      return false;
    }
    return connect(url, silent: silent);
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
    // Le jeton (audit 2026-11-10) est exigé par le serveur pour ce rattachement automatique.
    final hubUrl = gameCode != null && playerId != null && _jeton != null && !partieTerminee
        ? '$resolvedUrl/hubs/game?code=${Uri.encodeQueryComponent(gameCode!)}&playerId=${Uri.encodeQueryComponent(playerId!)}&jeton=${Uri.encodeQueryComponent(_jeton!)}'
        : '$resolvedUrl/hubs/game';
    _codeDansUrlHub = hubUrl.contains('?code=') ? gameCode : null;
    // Droits admin : portés par le joueur côté serveur, relus à chaque join/EtatCourant.
    isAdmin = false;
    _hub = HubConnectionBuilder().withUrl(hubUrl).withAutomaticReconnect().build()
      // Retour utilisateur (2026-11-01) : avec les défauts (ping 15 s, coupure après 30 s), une
      // connexion morte sans fermeture propre (Wi-Fi en veille) passait pour active une demi-minute
      // et les réponses partaient dans le vide — voir AddSignalR dans Program.cs côté serveur.
      ..keepAliveIntervalInMilliseconds = 5000
      ..serverTimeoutInMilliseconds = 15000;

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
        final codeTente = gameCode!;
        // Partie terminée (écran de fin) : on reste dans le salon pour recevoir un éventuel
        // « Rejouer », sans quitter l'écran de fin (audit 2026-11-10).
        final rejoint = await joinGame(codeTente, nom!, actualiserEcran: !partieTerminee);
        if (!rejoint && _dernierJoinRefuseParServeur) {
          // Code stocké devenu invalide (salon fermé, serveur redémarré : l'état est en mémoire) —
          // repli sur l'écran « rejoindre » avec une explication plutôt qu'un champ vide muet.
          await _oublierPartie();
          errorMessage = _dernierMessageJoin == 'Partie introuvable.'
              ? 'La partie $codeTente n\'existe plus (serveur redémarré ?) — rejoins le nouveau code.'
              : _dernierMessageJoin;
          screen = AppScreen.join;
          notifyListeners();
        } else if (!rejoint) {
          // Échec réseau juste après la connexion : on garde le code et on réessaie.
          _planifierRejoinApresReconnexionCourte();
        }
      } else {
        screen = AppScreen.join;
        notifyListeners();
      }
      return true;
    } catch (e) {
      // Retour utilisateur (2026-09-27, joueur "incapable de répondre") : un start() expiré par
      // [timeout] n'est PAS annulé — il pouvait aboutir plus tard en arrière-plan, laissant une
      // connexion zombie qui recevait encore les événements (questions affichées) alors que [_hub]
      // pointait déjà sur une autre connexion, morte, utilisée pour envoyer les réponses.
      final echoue = _hub;
      unawaited(echoue?.stop().catchError((_) {}));
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
      // Une reconnexion suit souvent un redémarrage du backend, donc un déploiement — voir
      // _demarrerVerificationsMiseAJour.
      _verifierMiseAJour();
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
      jokerDisponible = etat.jokerDisponible;
      afficherEcart = etat.afficherEcart;
      isAdmin = etat.estAdmin;
      // Audit 2026-11-10 : l'état de pause est renvoyé même entre deux rounds — une reprise
      // survenue pendant la coupure laissait sinon le téléphone bloqué « en pause ».
      paused = etat.enPause;
      partieEnCours = etat.etat == 'EnCours';
      if (etat.voteThemes != null) voteThemes = etat.voteThemes;
      appliquerEtatCourant(etat.etatCourant, actualiserEcran: false);
      notifyListeners();
    });

    // Audit 2026-11-10 — ce joueur vient d'être repris sur un autre appareil (même playerId et
    // jeton) : cette connexion n'est plus la sienne. On arrête de se reconnecter automatiquement.
    hub.on('SessionRemplacee', (_) {
      _rejoinRetryTimer?.cancel();
      _rejoinRetryTimer = null;
      _reinitialiserEtatPartie();
      unawaited(_oublierPartie());
      players.clear();
      isAdmin = false;
      showLeaderboard = false;
      errorMessage = 'Ta partie a été reprise sur un autre appareil.';
      screen = AppScreen.join;
      notifyListeners();
    });

    // Retour utilisateur (2026-10-17) — un joueur a quitté volontairement (GameHub.QuitterPartie) : au
    // lobby il disparaît, en partie il reste visible comme déconnecté (son score reste au classement).
    hub.on('PlayerLeft', (args) {
      final data = args![0] as Map<String, dynamic>;
      final id = data['playerId'] as String;
      if (data['retire'] as bool? ?? false) {
        players.removeWhere((p) => p.playerId == id);
        notifyListeners();
      } else {
        for (final p in players.where((p) => p.playerId == id)) {
          p.aQuitte = true;
        }
        _updatePlayerConnection(id, false);
      }
    });

    // Retour utilisateur (2026-10-17) — vote des thèmes au lobby, voir [voterThemes].
    hub.on('VoteThemesOuvert', (args) {
      final data = args![0] as Map<String, dynamic>;
      final themes = (data['themes'] as List<dynamic>? ?? []).cast<String>();
      final votesParJoueur = data['votesParJoueur'] as int;
      // Le serveur garde les votes encore valides à une réouverture — même filtre ici.
      final conserves = (voteThemes?.mesVotes ?? []).where(themes.contains).take(votesParJoueur).toList();
      voteThemes = VoteThemesEtat(ouvert: true, votesParJoueur: votesParJoueur, themes: themes, mesVotes: conserves);
      notifyListeners();
    });

    hub.on('VotesThemesMisAJour', (args) {
      votesDecompte = VotesThemesDecompte.fromJson(args![0] as Map<String, dynamic>);
      voteThemes?.ouvert = votesDecompte!.ouvert;
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
      final id = data['playerId'] as String;
      for (final p in players.where((p) => p.playerId == id)) {
        p.aQuitte = false;
      }
      _updatePlayerConnection(id, true);
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
      afficherEcart = serieIntro!.afficherEcart;
      // Refonte UI (lot 3) : le classement affiché en fin de série se referme à l'annonce de la
      // série suivante, comme sur la page host.
      showLeaderboard = false;
      screen = AppScreen.serieIntro;
      notifyListeners();
    });

    hub.on('RoundStarted', (args) {
      final data = args![0] as Map<String, dynamic>;
      currentRound = RoundStarted.fromJson(data);
      roundAnswered = false;
      envoiReponseEchoue = false;
      jokerErreur = null;
      reponseChoisie = null;
      repondants.clear();
      // V2, section 12.7 : null sur un round fraîchement diffusé (currentRound.jokerIndice l'est
      // toujours ici) — la lecture depuis l'objet plutôt qu'un null en dur garde un seul point de
      // vérité avec la branche reconnexion d'appliquerEtatCourant.
      jokerIndiceActuel = currentRound!.jokerIndice;
      lastRoundResult = null;
      errorMessage = null;
      phaseRecueA = DateTime.now();
      // Le serveur refuse StartRound en pause : un round qui démarre lève toute pause locale restée
      // bloquée (audit 2026-11-10), et referme le classement resté affiché par-dessus.
      paused = false;
      showLeaderboard = false;
      partieEnCours = true;
      screen = AppScreen.round;
      unawaited(Haptique.depart());
      notifyListeners();
    });

    hub.on('ScoreUpdate', (args) {
      final data = args![0] as Map<String, dynamic>;
      scoreUpdate = ScoreUpdate.fromJson(data);
      _syncOwnScore();
      notifyListeners();
    });

    // Diffusé à tout le groupe (pas seulement à l'écran public) — ne porte ni la réponse ni son
    // exactitude, voir PlayerAnsweredDto côté backend.
    // Refonte UI (lot 3) — télécommande admin, voir regieLibelleAction.
    hub.on('EtatRegie', (args) {
      final data = args![0] as Map<String, dynamic>;
      regieLibelleAction = data['libelleAction'] as String?;
      regieActionDisponible = data['actionDisponible'] as bool? ?? false;
      notifyListeners();
    });

    hub.on('PlayerAnswered', (args) {
      final data = args![0] as Map<String, dynamic>;
      if (repondants.add(data['playerId'] as String)) notifyListeners();
    });

    hub.on('RoundEnded', (args) {
      final data = args![0] as Map<String, dynamic>;
      lastRoundResult = RoundEnded.fromJson(data);
      _ajouterMorceauJoue(lastRoundResult!.trackId, lastRoundResult!.title, lastRoundResult!.artist);
      final mien = lastRoundResult!.resultats.where((r) => r.playerId == playerId).firstOrNull;
      if (mien != null) _vibrerResultat(mien.estCorrecte == true);
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
      partieTerminee = true;
      partieEnCours = false;
      notifyListeners();
    });

    // Déclenché par le host (RejouerPartie côté serveur) — même code, mêmes joueurs,
    // scores remis à zéro côté serveur. Pas de bouton côté joueur : seul le host décide.
    hub.on('GameRestarted', (_) {
      _reinitialiserEtatPartie();
      partieTerminee = false;
      // Audit 2026-11-10 (régression du correctif du playtest 2026-09-06) : GameEnded avait oublié
      // le code persisté — sans le réécrire ici, un redémarrage de l'app pendant la 2e manche
      // retombait sur un champ de code vide.
      final code = gameCode;
      if (code != null) unawaited(_prefs?.setString(_prefsGameCode, code));
      screen = AppScreen.lobby;
      notifyListeners();
    });

    // Retour utilisateur (2026-09-27) — le host a fermé ce salon pour en créer un nouveau (nouveau
    // code, voir GameHub.FermerSalon) : l'ancien code n'existe plus côté serveur, retour à l'écran
    // « rejoindre » en oubliant le code pour ne pas retenter un rejoin automatique dessus.
    hub.on('SalonFerme', (_) {
      _reinitialiserEtatPartie();
      voteThemes = null;
      votesDecompte = null;
      _rejoinRetryTimer?.cancel();
      _rejoinRetryTimer = null;
      unawaited(_oublierPartie());
      teamId = null;
      teams = [];
      players.clear();
      showLeaderboard = false;
      errorMessage = 'Le host a fermé le salon — rejoins le nouveau code.';
      screen = AppScreen.join;
      notifyListeners();
    });

    hub.on('BonusStakeOptions', (args) {
      final data = args![0] as Map<String, dynamic>;
      bonusStakeOptions = BonusStakeOptions.fromJson(data);
      bonusPalierSelectionne = null;
      bonusStakeEnvoyee = false;
      envoiReponseEchoue = false;
      phaseRecueA = DateTime.now();
      paused = false;
      showLeaderboard = false;
      partieEnCours = true;
      screen = AppScreen.bonusStake;
      unawaited(Haptique.depart());
      notifyListeners();
    });

    hub.on('BonusQuestionStarted', (args) => onBonusQuestionStarted(args![0] as Map<String, dynamic>));

    hub.on('BonusResult', (args) => onBonusResult(args![0] as Map<String, dynamic>));
  }

  /// État propre à une partie, remis à zéro par GameRestarted (même salon, scores à 0 côté serveur)
  /// et SalonFerme (salon supprimé). paused inclus : RejouerPartie remet EnPause à false côté
  /// serveur sans diffuser GameResumed — sans ça, un joueur resté "en pause" localement ne pouvait
  /// plus répondre à aucun round de la partie suivante.
  void _reinitialiserEtatPartie() {
    score = 0;
    paused = false;
    partieEnCours = false;
    phaseRecueA = null;
    serieIntro = null;
    currentRound = null;
    roundAnswered = false;
    reponseChoisie = null;
    repondants.clear();
    envoiReponseEchoue = false;
    jokerErreur = null;
    jokerDisponible = true;
    jokerIndiceActuel = null;
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
  }

  // Extraits de _registerHandlers en méthodes nommées (visibilité fichier, pas privées) pour être
  // exercables directement par des tests unitaires sans connexion SignalR réelle — voir
  // app/test/services/game_connection_test.dart et docs/refactor-decisions.md section 4.
  void onBonusQuestionStarted(Map<String, dynamic> data) {
    bonusQuestion = BonusQuestionStarted.fromJson(data);
    bonusAnswered = false;
    reponseChoisie = null;
    repondants.clear();
    envoiReponseEchoue = false;
    // Point de départ du décompte : l'écran d'intro du mode course en consomme une partie, déduite
    // à l'affichage de la question (voir AnswerPhaseScreen).
    phaseRecueA = DateTime.now();
    paused = false;
    showLeaderboard = false;
    _introCourseTimer?.cancel();
    unawaited(Haptique.depart());

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
    // Absent des résultats en mode course quand un autre joueur a répondu en premier : aucune
    // vibration dans ce cas, la mise est simplement récupérée.
    final mien = lastBonusResult!.resultats.where((r) => r.playerId == playerId).firstOrNull;
    if (mien != null) _vibrerResultat(mien.estCorrecte);
    screen = AppScreen.bonusResult;
    notifyListeners();
  }

  /// Double impulsion si juste, vibration longue si faux ou sans réponse.
  void _vibrerResultat(bool correct) {
    if (correct) {
      unawaited(Haptique.bonneReponse());
    } else {
      Haptique.erreur();
    }
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
    _cycleDeVie ??= AppLifecycleListener(onResume: _auRetourAuPremierPlan);
    _reconnectTimer = Timer.periodic(_delaiRetryReconnexion, (_) async {
      // Une tentative précédente encore en cours : ne pas en lancer une deuxième en parallèle, qui
      // remplacerait [_hub] pendant que la première peut encore aboutir (voir catch de [connect]).
      if (connecting) return;
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
        await _oublierPartie();
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
    _dernierJoinRefuseParServeur = false;
    _dernierMessageJoin = null;
    if (actualiserEcran) notifyListeners();

    final codeNormalise = code.trim().toUpperCase();
    final pseudoNettoye = pseudo.trim();
    final jetonConnu = codeNormalise == gameCode ? _jeton : _jetonPersistePour(codeNormalise);

    try {
      final result = await _hub!.invoke('JoinGame', args: [codeNormalise, pseudoNettoye, playerId!, jetonConnu ?? '']);
      final joinResult = JoinResult.fromJson(result as Map<String, dynamic>);

      if (!joinResult.success) {
        _dernierJoinRefuseParServeur = true;
        _dernierMessageJoin = joinResult.errorMessage ?? 'Impossible de rejoindre la partie.';
        if (actualiserEcran) {
          errorMessage = _dernierMessageJoin;
          notifyListeners();
        }
        return false;
      }

      final changementDeSalon = gameCode != codeNormalise;
      gameCode = codeNormalise;
      nom = pseudoNettoye;
      _jeton = joinResult.jeton ?? jetonConnu;
      score = joinResult.score;
      teamId = joinResult.teamId;
      teams = joinResult.teams;
      jokerDisponible = joinResult.jokerDisponible;
      afficherEcart = joinResult.afficherEcart;
      voteThemes = joinResult.voteThemes;
      isAdmin = joinResult.estAdmin;
      paused = joinResult.enPause;
      partieEnCours = joinResult.etat == 'EnCours';
      // Une partie terminée n'est jamais re-persistée (audit 2026-11-10, lobby fantôme) — sauf join
      // explicite d'un salon terminé, qui attend alors un « Rejouer » du host.
      partieTerminee = joinResult.etat == 'Termine';
      await _prefs?.setString(_prefsNom, pseudoNettoye);
      if (!partieTerminee) await _prefs?.setString(_prefsGameCode, codeNormalise);
      if (_jeton != null) await _prefs?.setString(_prefsJeton, '$codeNormalise:$_jeton');
      if (changementDeSalon) _rafraichirUrlHubApresChangementDeSalon();

      // Le serveur ne diffuse PlayerJoined qu'aux AUTRES joueurs déjà présents (GameHub.JoinGame)
      // — sans le roster complet renvoyé ici, un joueur ne voyait ni lui-même (retour
      // utilisateur), ni ceux ayant rejoint avant lui.
      players
        ..clear()
        ..addAll(joinResult.joueurs.map(
          (j) => PlayerInfo(playerId: j.playerId, nom: j.nom, estConnecte: j.estConnecte, teamId: j.teamId, aQuitte: j.aPartie),
        ));
      // Sur l'écran de fin, une micro-coupure ne doit pas ramener au salon.
      if (partieTerminee && screen == AppScreen.ended) {
        notifyListeners();
        return true;
      }
      appliquerEtatCourant(joinResult.etatCourant, actualiserEcran: actualiserEcran);
      notifyListeners();
      return true;
    } catch (e) {
      if (actualiserEcran) {
        errorMessage = messageErreur(e);
        notifyListeners();
      }
      return false;
    }
  }

  /// Vrai si le dernier [joinGame] a été refusé par le serveur (salon introuvable, jeton...) plutôt
  /// qu'interrompu par le réseau — seul ce cas justifie d'oublier le code mémorisé.
  bool _dernierJoinRefuseParServeur = false;
  String? _dernierMessageJoin;

  /// Rejoint un autre salon que celui porté par l'URL du hub : la connexion est recréée pour que
  /// les reconnexions transport suivantes rattachent bien CE salon (audit 2026-11-10).
  void _rafraichirUrlHubApresChangementDeSalon() {
    if (_codeDansUrlHub == null || _codeDansUrlHub == gameCode) return;
    final url = serverUrl;
    if (url == null) return;
    unawaited(Future<void>.delayed(Duration.zero, () => connect(url, timeout: _delaiReconnexionAuto, silent: true)));
  }

  /// Retour au premier plan : relance tout de suite une connexion perdue plutôt que d'attendre le
  /// prochain essai périodique (audit 2026-11-10).
  void _auRetourAuPremierPlan() {
    _verifierMiseAJour();
    final url = serverUrl;
    if (!connected && !connecting && url != null) {
      unawaited(connect(url, timeout: _delaiReconnexionAuto, silent: true));
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

    // Retour utilisateur (2026-11-01, "il faut appuyer plusieurs fois pour que ça soit pris en
    // compte") : une reconnexion survenue pendant l'envoi d'une réponse (submitAnswer en attente de
    // connexion dans _invoquerAvecReprise) renvoie dejaRepondu=false, le serveur n'ayant encore rien
    // reçu — l'écran se déverrouillait alors que l'envoi aboutissait juste après, et le joueur devait
    // réappuyer. Sur la même question, l'état local verrouillé est conservé : en cas d'échec réel de
    // l'envoi, c'est submitAnswer/selectStake/submitBonusAnswer qui rend la main.
    final memeQuestion = switch (etat.phase) {
      PhaseJoueur.roundClassique => etat.round != null && etat.round!.roundId == currentRound?.roundId,
      PhaseJoueur.bonusMise => etat.bonusMise != null && etat.bonusMise!.roundId == bonusStakeOptions?.roundId,
      PhaseJoueur.bonusQuestion => etat.bonusQuestion != null && etat.bonusQuestion!.roundId == bonusQuestion?.roundId,
      PhaseJoueur.aucune => false,
    };
    if (!memeQuestion) {
      reponseChoisie = null;
      repondants.clear();
      // Restes de la question précédente (audit 2026-11-10) : bandeau d'erreur rouge, tuile de mise
      // « verrouillée » d'un ancien bonus.
      envoiReponseEchoue = false;
      jokerErreur = null;
      bonusPalierSelectionne = null;
      phaseRecueA = DateTime.now();
    }
    partieEnCours = true;

    switch (etat.phase) {
      case PhaseJoueur.roundClassique:
        roundAnswered = etat.dejaRepondu || (memeQuestion && roundAnswered);
        currentRound = etat.round;
        // V2, section 12.7 : non null seulement si ce joueur avait déjà utilisé son joker sur ce
        // round avant la coupure — rejoue alors le même indice plutôt qu'un nouveau tirage.
        jokerIndiceActuel = etat.round?.jokerIndice;
        lastRoundResult = null;
        screen = AppScreen.round;
      case PhaseJoueur.bonusMise:
        bonusStakeEnvoyee = etat.dejaRepondu || (memeQuestion && bonusStakeEnvoyee);
        bonusStakeOptions = etat.bonusMise;
        screen = AppScreen.bonusStake;
      case PhaseJoueur.bonusQuestion:
        bonusAnswered = etat.dejaRepondu || (memeQuestion && bonusAnswered);
        bonusQuestion = etat.bonusQuestion;
        screen = AppScreen.bonusQuestion;
      case PhaseJoueur.aucune:
        // Jamais renvoyé par le serveur en pratique (GameHub.ConstruireEtatCourantJoueur renvoie
        // null plutôt qu'un DTO à Phase.Aucune) — présent pour l'exhaustivité du switch.
        if (actualiserEcran) screen = AppScreen.lobby;
    }
  }

  /// Retour utilisateur (2026-10-17) — vote des thèmes ouvert par le host au lobby. null si aucun
  /// vote n'a été ouvert. [votesDecompte] : décompte public (voix par thème, qui a voté), jamais qui a
  /// voté quoi.
  VoteThemesEtat? voteThemes;
  VotesThemesDecompte? votesDecompte;
  bool voteEnvoiEnCours = false;

  /// Remplace entièrement les votes de ce joueur (il peut changer d'avis tant que le vote est
  /// ouvert) — le serveur refuse au-delà de votesParJoueur ou hors des thèmes proposés.
  Future<void> voterThemes(List<String> themes) async {
    final vote = voteThemes;
    if (vote == null || !vote.ouvert || voteEnvoiEnCours) return;
    final precedents = vote.mesVotes;
    vote.mesVotes = themes;
    voteEnvoiEnCours = true;
    notifyListeners();
    try {
      final ok = await _hub!.invoke('VoterThemes', args: [themes]) as bool? ?? false;
      if (!ok) vote.mesVotes = precedents;
    } catch (_) {
      vote.mesVotes = precedents;
    } finally {
      voteEnvoiEnCours = false;
      notifyListeners();
    }
  }

  /// Retour utilisateur (2026-10-17) — « impossible de quitter un salon » : le code persisté faisait
  /// rejoindre automatiquement la même partie à chaque relance de l'app. Prévient le serveur
  /// (GameHub.QuitterPartie : au lobby le joueur est retiré, en partie son score est gardé et il pourra
  /// revenir avec le même code), oublie le code, puis rouvre une connexion neuve au même serveur sans
  /// ?code&playerId dans l'URL, prête pour rejoindre un autre salon. Best-effort côté serveur : même si
  /// l'appel échoue (réseau coupé), le joueur se retrouve libre localement.
  Future<void> quitterPartie() async {
    try {
      if (connected) await _hub?.invoke('QuitterPartie');
    } catch (_) {
      // Rien à faire : le joueur quitte quand même localement.
    }

    _introCourseTimer?.cancel();
    _rejoinRetryTimer?.cancel();
    _rejoinRetryTimer = null;
    await _oublierPartie();
    _reinitialiserEtatPartie();
    partieTerminee = false;
    teamId = null;
    teams = [];
    players.clear();
    voteThemes = null;
    votesDecompte = null;
    isAdmin = false;
    showLeaderboard = false;
    errorMessage = null;

    final url = serverUrl;
    if (url != null) {
      await connect(url, timeout: _delaiReconnexionAuto, silent: true);
    } else {
      screen = AppScreen.connect;
      notifyListeners();
    }
  }

  /// Autorisé à tout moment (pas seulement au lobby) — l'état local est mis à jour via
  /// l'événement PlayerTeamChanged plutôt qu'ici, pour rester cohérent avec ce que voient
  /// les autres joueurs et le host.
  Future<void> joinTeam(String teamId) async {
    try {
      await _hub!.invoke('JoinTeam', args: [teamId]);
    } catch (e) {
      errorMessage = messageErreur(e);
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
      adminError = messageErreur(e);
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
      adminError = messageErreur(e);
      notifyListeners();
    }
  }

  Future<void> adminResumeGame() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('ResumeGame');
    } catch (e) {
      adminError = messageErreur(e);
      notifyListeners();
    }
  }

  Future<void> adminShowLeaderboard() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('ShowLeaderboard');
    } catch (e) {
      adminError = messageErreur(e);
      notifyListeners();
    }
  }

  Future<void> adminEndGame() async {
    if (!isAdmin) return;
    try {
      await _hub?.invoke('EndGame');
    } catch (e) {
      adminError = messageErreur(e);
      notifyListeners();
    }
  }

  /// Refonte UI (lot 3) — télécommande : ce que ferait « Action suivante » sur la page host à cet
  /// instant (libellé du bouton, disponible ou non), publié par le host et relayé par le serveur
  /// aux seuls admins (voir GameHub.PublierEtatRegie). null tant que le host n'a rien publié.
  String? regieLibelleAction;
  bool regieActionDisponible = false;

  /// Relaie une commande à la page host, qui l'exécute comme son raccourci clavier ("suivant" =
  /// Espace, "reecouter" = R) — voir GameHub.EnvoyerCommandeHost.
  Future<void> adminCommandeHost(String commande) async {
    if (!isAdmin) return;
    adminError = null;
    try {
      await _hub?.invoke('EnvoyerCommandeHost', args: [commande]);
    } catch (e) {
      adminError = messageErreur(e);
      notifyListeners();
    }
  }

  /// Refonte UI (lot 3) — option de la partie (voir JoinResult.afficherEcart).
  bool afficherEcart = true;

  /// Écart de points avec le joueur (ou l'équipe, en mode équipe) juste devant, pour l'écran
  /// d'attente : 0 = en tête (éventuellement à égalité), > 0 = points de retard. null si l'option
  /// est désactivée ou si aucun score n'a encore été reçu. Jamais le nom ni la place de personne
  /// (choix utilisateur : seul le premier sait qu'il est premier).
  int? get ecartAvecJoueurDevant {
    final scores = scoreUpdate;
    if (!afficherEcart || scores == null) return null;

    final equipes = scores.equipes;
    final List<int> autres;
    final int moi;
    if (equipes != null && equipes.isNotEmpty && teamId != null) {
      final monEquipe = equipes.where((e) => e.teamId == teamId).firstOrNull;
      if (monEquipe == null) return null;
      moi = monEquipe.score;
      autres = equipes.where((e) => e.teamId != teamId).map((e) => e.score).toList();
    } else {
      final mien = scores.joueurs.where((j) => j.playerId == playerId).firstOrNull;
      if (mien == null) return null;
      moi = mien.score;
      autres = scores.joueurs.where((j) => j.playerId != playerId).map((j) => j.score).toList();
    }

    final devant = autres.where((s) => s > moi);
    return devant.isEmpty ? 0 : devant.reduce((a, b) => a < b ? a : b) - moi;
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
      flagError = messageErreur(e);
      notifyListeners();
      return false;
    }
  }

  /// Un seul essai par round — déjà appliqué côté serveur (voir RoundService.SoumettreReponse),
  /// mais l'UI doit refléter l'état immédiatement pour ne pas laisser croire qu'une
  /// deuxième soumission est possible.
  Future<void> submitAnswer(String reponse) async {
    // Une réponse vide consommait l'unique essai avec pénalité (audit 2026-11-10).
    if (roundAnswered || paused || reponse.trim().isEmpty) return;

    roundAnswered = true;
    envoiReponseEchoue = false;
    reponseChoisie = reponse;
    Haptique.validation();
    notifyListeners();

    final roundId = currentRound?.roundId;
    try {
      // roundId omis si currentRound est encore null (ne devrait pas arriver en usage réel — cet
      // écran n'est affiché qu'après réception de RoundStarted) : le serveur traite alors la
      // soumission comme un roundId périmé et l'ignore silencieusement, plutôt que de planter.
      await _invoquerAvecReprise('SubmitAnswer', [
        {'roundId': ?roundId, 'reponse': reponse}
      ]);
    } catch (e) {
      // Toujours sur le même round : rendre la main au joueur pour qu'il réessaie, plutôt que de
      // laisser "Réponse envoyée" affiché alors que le serveur n'a rien reçu.
      if (currentRound?.roundId == roundId) {
        roundAnswered = false;
        reponseChoisie = null;
        envoiReponseEchoue = true;
        notifyListeners();
      }
    }
  }

  static const _tentativesEnvoi = 3;

  /// 8 s par tentative (24 s au total, plus qu'une fenêtre de réponse usuelle) plutôt que 4 s
  /// (retour utilisateur 2026-11-01) : la reconnexion automatique de SignalR espace ses essais de
  /// 0, 2, 10 puis 30 s — au-delà du 2e essai, les 12 s précédentes expiraient avant le retour de
  /// la connexion, l'envoi était déclaré en échec et le joueur devait réappuyer.
  @visibleForTesting
  Duration delaiAttenteConnexion = const Duration(seconds: 8);

  /// Retour utilisateur (2026-09-27, joueur "incapable de répondre", pénalité d'absence à chaque
  /// round) : l'envoi d'une réponse passait par un simple `_hub?.invoke`, qui ne faisait rien si
  /// [_hub] était null et échouait sans rien afficher si la connexion était en pleine reconnexion.
  /// Attend désormais que la connexion soit rétablie (borné) et retente l'appel. Une réponse déjà
  /// reçue par le serveur mais dont l'accusé s'est perdu est simplement ignorée au second envoi
  /// ("déjà répondu" côté serveur), donc la reprise est sans risque.
  Future<Object?> _invoquerAvecReprise(String methode, List<Object> args) async {
    Object? derniereErreur;
    for (var tentative = 0; tentative < _tentativesEnvoi; tentative++) {
      final hub = await _attendreConnexion();
      if (hub == null) {
        derniereErreur = StateError('Connexion au serveur indisponible');
        continue;
      }
      try {
        return await hub.invoke(methode, args: args);
      } catch (e) {
        derniereErreur = e;
        await Future<void>.delayed(const Duration(milliseconds: 500));
      }
    }
    throw derniereErreur ?? StateError('Envoi impossible');
  }

  Future<HubConnection?> _attendreConnexion() async {
    final limite = DateTime.now().add(delaiAttenteConnexion);
    while (true) {
      final hub = _hub;
      if (hub != null && hub.state == HubConnectionState.Connected) return hub;
      if (DateTime.now().isAfter(limite)) return null;
      await Future<void>.delayed(const Duration(milliseconds: 250));
    }
  }

  /// V2, section 12.7 — un joker par joueur et par partie complète, round classique uniquement
  /// (jamais en bonus, voir AnswerPhaseScreen qui n'affiche le bouton que côté classique). Un refus
  /// serveur (déjà répondu/déjà utilisé/en pause/round périmé) reste une HubException : jokerDisponible
  /// n'est mis à jour qu'en cas de succès, pour ne pas rouvrir le bouton sur un état qui restera refusé.
  bool _jokerEnCours = false;

  Future<void> utiliserJoker() async {
    if (!jokerDisponible || roundAnswered || paused || currentRound == null || _jokerEnCours) return;

    jokerErreur = null;
    _jokerEnCours = true;
    final roundId = currentRound!.roundId;
    try {
      // Pas de reprise automatique (contrairement à submitAnswer) : un premier appel réussi dont
      // l'accusé se perd serait refusé au second ("déjà utilisé") et afficherait une erreur à tort.
      final hub = await _attendreConnexion();
      if (hub == null) throw StateError('Connexion au serveur indisponible');
      final result = await hub.invoke('UtiliserJoker', args: [roundId]);
      jokerIndiceActuel = JokerIndice.fromJson(result as Map<String, dynamic>);
      jokerDisponible = false;
      notifyListeners();
    } catch (e) {
      // Retour utilisateur (2026-09-27) : l'échec partait dans errorMessage, jamais affiché sur
      // l'écran de réponse — le joueur ne savait pas que son joker n'avait pas marché.
      if (currentRound?.roundId == roundId) {
        jokerErreur = e is StateError
            ? 'Joker non envoyé (connexion) — réessaie.'
            : 'Joker refusé : ${messageErreur(e)}';
        notifyListeners();
      }
    } finally {
      _jokerEnCours = false;
    }
  }

  /// Message d'échec du dernier appel à [utiliserJoker], affiché sur l'écran de réponse — remis à
  /// null à chaque nouveau round.
  String? jokerErreur;

  /// Choix du palier de mise, à l'aveugle avant de découvrir la question — un seul essai
  /// par round bonus, déjà appliqué côté serveur (voir BonusRoundService.EnregistrerMise).
  Future<void> selectStake(int palierIndex) async {
    if (bonusStakeEnvoyee || paused) return;

    bonusPalierSelectionne = palierIndex;
    bonusStakeEnvoyee = true;
    Haptique.validation();
    notifyListeners();

    final roundId = bonusStakeOptions?.roundId;
    try {
      await _invoquerAvecReprise('SelectStake', [
        {'roundId': ?roundId, 'palierIndex': palierIndex}
      ]);
    } catch (e) {
      // Voir submitAnswer — rend la main pour un nouveau choix plutôt que de laisser le palier
      // "safe" par défaut s'appliquer à l'insu du joueur.
      if (bonusStakeOptions?.roundId == roundId) {
        bonusPalierSelectionne = null;
        bonusStakeEnvoyee = false;
        envoiReponseEchoue = true;
        notifyListeners();
      }
    }
  }

  Future<void> submitBonusAnswer(String reponse) async {
    if (bonusAnswered || paused || reponse.trim().isEmpty) return;

    bonusAnswered = true;
    envoiReponseEchoue = false;
    reponseChoisie = reponse;
    Haptique.validation();
    notifyListeners();

    final roundId = bonusQuestion?.roundId;
    try {
      await _invoquerAvecReprise('SubmitBonusAnswer', [
        {'roundId': ?roundId, 'reponse': reponse}
      ]);
    } catch (e) {
      // Voir submitAnswer.
      if (bonusQuestion?.roundId == roundId) {
        bonusAnswered = false;
        reponseChoisie = null;
        envoiReponseEchoue = true;
        notifyListeners();
      }
    }
  }

  @override
  void dispose() {
    _introCourseTimer?.cancel();
    _reconnectTimer?.cancel();
    _rejoinRetryTimer?.cancel();
    _verificationMiseAJourTimer?.cancel();
    _cycleDeVie?.dispose();
    _hub?.stop();
    super.dispose();
  }
}
