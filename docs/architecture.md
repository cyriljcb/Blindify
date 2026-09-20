# Architecture — Blindtest v2 (Blindify)

Document de synthèse des décisions d'architecture prises pendant le brainstorming. Sert de référence pour l'implémentation.

## 1. Vue d'ensemble

Évolution du projet Blindify existant (Spring Boot + Angular) vers une nouvelle stack :

- **Backend** : ASP.NET Core + SignalR (réécriture complète, remplace Spring Boot)
- **Stockage audio** : fichiers locaux téléchargés depuis YouTube (playlist récupérée via l'API Spotify pour les métadonnées), stockés sur un HDD 2 To branché au Raspberry Pi
- **Séparation données éditoriales / état runtime** : `tracks.json` (métadonnées, édité via le tableur) et `stats.json` (compteurs runtime type `playCount`) sont deux fichiers distincts, pour ne jamais risquer d'écraser l'un en manipulant l'autre (voir section 4)
- **Client host** : page web ouverte sur un ordinateur, affiche l'état du jeu et joue l'audio via une seule enceinte physique
- **Client joueur** : app Flutter sur téléphone, sert de buzzer/interface de réponse, ne reçoit jamais l'audio
- **Usage** : réseau local uniquement (WiFi maison), pas d'accès distant prévu

### Pourquoi pas l'API Spotify pour l'audio

L'API Spotify ne permet pas de télécharger l'audio des morceaux, et même les extraits 30s (`preview_url`) sont dépréciés depuis fin 2024 et peu fiables. L'API Spotify est donc utilisée uniquement pour la découverte de playlist et les métadonnées (titre, artiste, album, genres). L'audio est ensuite téléchargé depuis YouTube (usage strictement privé/local — techniquement contraire aux ToS YouTube, à garder en tête).

## 2. Topologie de déploiement

```
┌─────────────────────────────┐
│      Raspberry Pi 5          │
│  ┌────────────────────────┐  │
│  │ Backend ASP.NET Core    │  │
│  │ + SignalR Hub           │  │
│  └────────────────────────┘  │
│  ┌────────────────────────┐  │
│  │ tracks.json (source de  │  │
│  │ vérité, chargé en RAM)  │  │
│  └────────────────────────┘  │
│  ┌────────────────────────┐  │
│  │ HDD 2 To — fichiers     │  │
│  │ audio (MP3 ~192 kbps)   │  │
│  └────────────────────────┘  │
└──────────────┬───────────────┘
               │ réseau local (WiFi)
     ┌─────────┼─────────────┐
     │                       │
┌────▼─────────┐    ┌────────▼────────┐
│  PC (host)    │    │  Téléphones     │
│  page web,    │    │  app Flutter,   │
│  audio local  │    │  buzzer/réponse │
│  sur enceinte │    │  pas d'audio    │
└───────────────┘    └─────────────────┘
```

Le backend reste toujours actif sur le Raspberry Pi (déjà utilisé comme homelab). Aucune synchronisation de fichiers à faire avant une partie : le PC host et les téléphones se connectent simplement à l'IP du Pi sur le réseau local.

### Dockerisation

Seul le **backend** est dockerisé. Le frontend (app Flutter et page web du host) n'est jamais construit/transformé par Docker — ce sont de simples fichiers consommés nativement (téléphone / navigateur) via le réseau. La dockerisation ne change donc rien au modèle de jeu ni au contrat SignalR ; c'est purement une question de déploiement.

Depuis le retour utilisateur du 2026-08-24, `host/` (page web du panneau de contrôle) est monté en volume comme `tracks.json`/audio/covers et servi tel quel en fichiers statiques par le backend — pas besoin de gérer ce dossier séparément sur le PC du host, un navigateur pointé sur `http://<ip-du-pi>:5000/` suffit (`display.html` à `http://<ip-du-pi>:5000/display.html`). Ça reste de simples fichiers statiques, aucune étape de build : ce n'est pas "dockeriser le frontend" au sens de CLAUDE.md, juste les servir au même titre que `/files` ci-dessous. Voir `docs` : ce mapping est optionnel (`Host:StaticPath` absent ou dossier introuvable = ignoré silencieusement), donc sans effet sur les tests d'intégration (`WebApplicationFactory`) ni sur qui lance juste l'API sans avoir `host/` sous la main.

Points d'attention :

- **Les données ne sont jamais copiées dans l'image** : `tracks.json`, les fichiers audio et les covers sont montés en volume depuis le HDD 2 To du Pi, pas *baked* dans l'image Docker. Ça permet d'ajouter des morceaux sans reconstruire l'image.
- **Port SignalR exposé sur le LAN** : publication du port du conteneur vers le réseau local (ex. `-p 5000:8080`), pas besoin de tunnel externe (ngrok, utilisé sur l'ancien Blindify) puisque l'usage reste local.
- **Politique de redémarrage** : `restart: unless-stopped` pour survivre à un reboot du Pi, cohérent avec le reste du homelab.
- **CORS** : le backend doit autoriser les origines des clients LAN (IP du PC host, éventuellement de l'app Flutter si elle passe par du HTTP avant l'upgrade WebSocket) — pas besoin d'un CORS ouvert à tout internet vu l'usage strictement local.

`docker-compose.yml` (à la racine du repo, voir ce fichier pour la version à jour) :

```yaml
services:
  backend:
    build: ./backend
    container_name: blindify-backend
    restart: unless-stopped
    ports:
      - "5000:8080"
    volumes:
      - /mnt/hdd2to/blindify/tracks.json:/data/tracks.json:ro
      - /mnt/hdd2to/blindify/stats.json:/data/stats.json
      - /mnt/hdd2to/blindify/audio:/data/audio:ro
      - /mnt/hdd2to/blindify/covers:/data/covers:ro
      - ./host:/host:ro
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Data__TracksPath=/data/tracks.json
      - Data__StatsPath=/data/stats.json
      - Data__AudioPath=/data/audio
      - Data__CoversPath=/data/covers
      - Data__RootPath=/data
      - Host__StaticPath=/host
```

`Data__RootPath` est servi en fichiers statiques sous `/files` par le backend — c'est ce qui permet au host de lire l'audio par HTTP (`GET /files/audio/xxx.mp3`), les chemins de `tracks.json` étant déjà relatifs à cette racine. Seul le host y accède, jamais les joueurs. `Host__StaticPath` (`./host` monté sur `/host`) est servi à la racine (`/`) — `index.html` en document par défaut, `display.html` et les autres fichiers (`app.js`, `style.css`, `vendor/`...) à leur chemin habituel.

Les volumes audio/covers/host sont montés en lecture seule (`ro`) côté conteneur — les scripts de préparation des données (sync Spotify, téléchargement YouTube, export/import CSV) et les modifications de `host/` se font directement sur le disque/Git, en dehors de Docker, donc le conteneur n'a besoin que de lire.

Le backend reste toujours actif sur le Raspberry Pi (déjà utilisé comme homelab). Aucune synchronisation de fichiers à faire avant une partie : le PC host et les téléphones se connectent simplement à l'IP du Pi sur le réseau local.

### Redémarrage à distance (retour utilisateur)

`POST /api/admin/restart` (payload `{ "password": "..." }`) permet de relancer le backend depuis la page host sans accès physique/SSH au Pi. N'arrête que le **process** ASP.NET Core (`IHostApplicationLifetime.StopApplication()`) — c'est la politique `restart: unless-stopped` ci-dessus qui relance ensuite le conteneur, pas cet endpoint. Protégé par un mot de passe (`Admin:RestartPassword`, variable d'environnement `Admin__RestartPassword` / `BLINDIFY_RESTART_PASSWORD` côté `docker-compose.yml`) : vide par défaut, l'endpoint répond alors `503` et reste désactivé — le réseau local n'est pas jugé assez fermé pour exposer un arrêt de process sans contrôle. Comparaison mot de passe à temps constant (`CryptographicOperations.FixedTimeEquals`). Toute partie en cours est perdue (état 100 % en mémoire, pas de persistance) : la page host affiche un avertissement explicite avant de demander confirmation.

### Contrôle admin depuis l'app Flutter (retour utilisateur)

`GameHub.AuthenticateAdmin(password)` permet à une connexion déjà associée à une partie (JoinGame préalable) de déclencher pause/reprise/tableau général/fin de partie depuis l'app Flutter (réglages), sans avoir à se déplacer jusqu'au PC host. Protégé par un mot de passe dédié (`Admin:RemoteControlPassword`, variable d'environnement `Admin__RemoteControlPassword` / `BLINDIFY_ADMIN_PASSWORD` côté `docker-compose.yml`), **distinct** de `Admin:RestartPassword` — même défaut vide = désactivé, même comparaison à temps constant. Un client authentifié rejoint `GameSession.AdminConnectionIds` (ensemble, pas un champ unique) : contrairement à `RejoinAsHost`/`HostConnectionId`, plusieurs admins peuvent coexister avec le host web sans jamais lui retirer la main — les actions restant exclusives au host (`StartRound`, `ConfigurerPartie`, `CreateGame`, lecture audio) continuent de passer par `ResoudreSessionHost`, celles ouvertes à l'admin (`PauseGame`/`ResumeGame`/`ShowLeaderboard`/`EndGame`) par `ResoudreSessionHostOuAdmin`. Ces actions ne font que diffuser l'évènement habituel (`GamePaused`, etc.) à tout le groupe — le host web réagit exactement comme s'il avait cliqué lui-même, l'audio ne transite jamais par le téléphone. Non persisté : l'authentification est à refaire à chaque nouvelle connexion SignalR (déconnexion/relance de l'app).

### Nom local au lieu de l'IP (mDNS/Bonjour)

Retour utilisateur (2026-08-25) : taper l'IP du Pi depuis un iPhone est pénible. Pas besoin d'un vrai serveur DNS local (overkill pour un usage familial) — le mDNS (Bonjour) suffit et est nativement supporté par iOS/Safari et macOS, sans rien installer côté client.

- Sur le Pi : `sudo raspi-config` → *System Options* → *Hostname*, renommer en `pi` (avahi-daemon, qui fait tourner le mDNS, est généralement déjà présent et actif par défaut sur Raspberry Pi OS — vérifier avec `systemctl status avahi-daemon`, sinon `sudo apt install avahi-daemon`).
- L'app devient joignable en `http://pi.local:5000` (panneau de contrôle) et `http://pi.local:5000/display.html` (écran public), à la place de l'IP.
- Config système sur le Pi, **aucun changement côté backend/Docker** : avahi tourne sur l'hôte, pas dans le conteneur, et n'a besoin de rien savoir du port publié.
- Limite : dépend du support mDNS du réseau Wi-Fi — impeccable sur une box/routeur familial classique, plus capricieux si le réseau isole les clients entre eux (peu probable en usage domestique).

### Mettre à jour l'app Android sans câble

Retour utilisateur (2026-08-27) : mettre à jour l'app sur les téléphones des joueurs obligeait à rebrancher chacun sur le poste qui fait tourner le serveur pendant les sessions. Puisque `host/` est déjà servi en statique à la racine (voir "Dockerisation" ci-dessus, actif aussi quand le serveur tourne en dev sur ce poste), il suffit d'y déposer l'APK.

**V2** : `app/scripts/build_release.ps1` fait le build, la copie vers `host/blindify.apk` **et** l'écriture de `host/apk_version.json` en une seule commande (lit `version: X.Y.Z+N` directement dans `pubspec.yaml`, donc les deux fichiers ne peuvent plus désynchroniser comme avec l'ancienne procédure manuelle en plusieurs étapes) :

```
cd app && .\scripts\build_release.ps1
```

Chaque téléphone Android visite `http://<ip-du-poste>:5000/blindify.apk` (ou `http://pi.local:5000/blindify.apk` une fois le mDNS en place) depuis son navigateur et installe (autoriser "sources inconnues" une fois par téléphone). Ni `host/blindify.apk` ni `host/apk_version.json` ne sont commités (voir `.gitignore` — état de déploiement, pas du code, et trop volumineux/vite obsolète pour l'APK). `Program.cs` mappe explicitement `.apk` → `application/vnd.android.package-archive` pour ce dossier, sans quoi le middleware de fichiers statiques renvoie 404 (extension absente du `FileExtensionContentTypeProvider` par défaut). Signé avec la clé debug du poste (`build.gradle`, stable d'un build à l'autre sur cette machine) : les mises à jour s'installent par-dessus sans désinstallation préalable.

**Détection automatique d'une mise à jour disponible** (retour utilisateur) : l'app compare son propre build (`PackageInfo`, reflète `pubspec.yaml` au moment du build) à `host/apk_version.json` — un bandeau apparaît dès le lancement si le serveur a un build plus récent (voir `services/update_checker.dart`).

Pas de solution équivalente pour iPhone sans compte Apple Developer/Xcode — voir point 9 du retour playtest 2026-08-24 (mémoire) pour la piste web (`flutter build web`, PWA via Safari).

## 3. Pipeline de préparation des données

1. **Récupération playlist** : appel API Spotify pour obtenir les morceaux d'une playlist (titre, artiste, album, ID Spotify).
2. **Enrichissement automatique des genres** : pour chaque artiste unique, appel batché (jusqu'à 50 artistes/requête) à `GET /artists` pour récupérer le champ `genres`. Peu de requêtes nécessaires même pour ~1000 morceaux.
3. **Tags thématiques manuels/semi-automatiques** : les genres Spotify (champ `genres`, trop nombreux/bruités — ~220 valeurs distinctes sur le catalogue actuel, voir section 6) ne conviennent pas tels quels à un sélecteur de thème joueur. `tags` regroupe ça en catégories grossières, peuplées via un passage assisté (script de bucketing par mots-clés sur `genres`, avec table d'exceptions pour les cas ambigus — ex. "singer-songwriter"/"folk" couvrent en pratique des artistes anglophones malgré l'intuition, pas de la chanson française) : une décennie (`annees-1970` … `annees-2020`, `avant-1970`) déduite de `year`, un genre large (`pop`, `rock`, `metal`, `rap`, `electro`, `rnb-funk-jazz`, `variete-francaise`, `latino`, `monde`) déduit de `genres`, plus des tags ad hoc posés à la main (`disney`). Un morceau peut porter plusieurs tags à la fois (ex. `["annees-2010", "variete-francaise"]`). Toujours avec relecture humaine du résultat avant mise en prod, le bucketing par mots-clés reste faillible sur les cas rares.
4. **Téléchargement audio** : pour chaque morceau, recherche + téléchargement YouTube (ex. via yt-dlp), fichier stocké sur le HDD, chemin enregistré dans `tracks.json`.
   - **Validation du matching** : parmi les résultats de recherche, ceux dont le titre sent la version live/acoustique/remix/cover sont écartés en priorité (`data/scripts/live_keywords.py`, retour utilisateur du 2026-08-25 — une version live coupée à la bonne durée n'était jamais repérée par le seul filtre de durée ci-dessous). Le candidat retenu doit ensuite avoir une durée proche de `durationMs` (Spotify), tolérance ±5-10s. Tout écart au-delà flague le morceau comme "à vérifier manuellement" plutôt que de l'intégrer tel quel au catalogue.
   - **Audit du catalogue déjà téléchargé** : `data/scripts/audit_live_versions.py` récupère (sans les télécharger) les titres YouTube réels des morceaux déjà présents dans `tracks.json` et exporte en CSV ceux qui matchent un mot-clé suspect — pour repérer après coup ce que le filtre ci-dessus n'aurait pas attrapé avant sa mise en place. Résultats mis en cache (`output/live_audit_cache.json`), lecture seule sur `tracks.json`.
5. **Téléchargement de la pochette d'album** : Spotify fournit l'URL de la cover via `album.images` (plusieurs résolutions). Téléchargée une fois et stockée localement à côté du fichier audio, chemin enregistré dans `tracks.json` — utilisée pour l'esthétique des écrans de jeu (écran de révélation, tableau général, etc.).

### Estimation espace disque

Pour ~1000 morceaux en MP3 192 kbps (largement suffisant pour un blindtest, surtout si seuls les 15-20 premières secondes sont jouées) : **~5 Go au total**. Le HDD 2 To est largement surdimensionné pour ce cas d'usage.

## 4. Schéma `tracks.json`

```json
{
  "id": "a1b2c3",
  "title": "Under the Sea",
  "artist": "Samuel E. Wright",
  "album": "The Little Mermaid",
  "spotifyId": "3n3Ppam7vgaVa1iaRUc9Lp",
  "youtubeId": "PT2_F-1esPk",
  "durationMs": 174000,
  "genres": ["disney", "soundtrack"],
  "tags": ["disney", "annees-1990"],
  "trapWith": ["idAutreMorceau1", "idAutreMorceau2"],
  "trapTextArtist": null,
  "year": 1989,
  "filePath": "audio/a1b2c3.mp3",
  "coverPath": "covers/a1b2c3.jpg",
  "refrainStartMs": null,
  "addedAt": "2026-08-06T10:00:00Z"
}
```

- `genres` : rempli automatiquement depuis Spotify (par artiste).
- `tags` : thèmes personnalisés (décennie + genre large + ad hoc), remplis manuellement ou semi-automatiquement — voir section 3, point 3.
- `trapWith` : IDs de morceaux fréquemment confondus (ex. Axel F / Crazy Frog), utilisés pour générer des QCM pièges.
- `trapTextArtist` : optionnel, `null` par défaut. Leurre texte **inventé à la main** pour la cible Auteur (ex. Bastille - Pompéi -> "Baptiste") — contrairement à `trapWith`, ne référence aucun morceau réel du catalogue, juste un nom d'artiste plausible mais fictif affiché à la place d'un distracteur tiré au sort. Voir section 6 (probabilité dédiée, volontairement basse pour ne pas devenir injuste).
- `coverPath` : pochette d'album téléchargée localement depuis Spotify, utilisée sur les écrans de jeu pour l'esthétique.
- `title` : nettoyé à l'import (voir section 3) des suffixes d'édition ("- Radio Edit", "- ... Remix", "(feat. ...)") — trop de bruit dans le titre le rend impossible à taper en mode `TapeReponse`/`PremiereLettre`.
- `refrainStartMs` : optionnel, `null` par défaut. Deviné automatiquement à l'import (voir section 3, analyse de similarité audio) ou renseigné manuellement — point de départ (ms) où le host saute pour jouer le refrain **au reveal** (une fois que tout le monde a répondu), pas pendant la découverte du round qui reste toujours jouée depuis le début du fichier.

**Stockage** : `tracks.json` = source de vérité unique pour les métadonnées éditoriales, versionnable avec Git, chargé en mémoire par le backend au démarrage (`List<Track>` + LINQ pour le filtrage). Pas de base de données pour l'instant — largement suffisant pour ce volume, à réévaluer seulement si le catalogue dépasse les dizaines de milliers de morceaux ou si des écritures concurrentes deviennent nécessaires.

### `stats.json` — compteurs runtime, séparés de `tracks.json`

`playCount` (et tout autre compteur alimenté pendant une partie) vit dans un fichier séparé, `data/stats.json`, indexé par `id` de morceau :

```json
{
  "a1b2c3": { "playCount": 3 }
}
```

**Raison de la séparation** : le backend tourne en continu (section 2) et doit persister `playCount` sur disque pour survivre à ses redémarrages — il a donc forcément besoin d'écrire quelque part. Si ce compteur vivait dans `tracks.json`, le backend aurait besoin d'un accès en écriture sur ce fichier, ce qui entrerait en collision avec le script d'import CSV (section 3bis) qui réécrit `tracks.json` en entier — potentiellement à tout moment, puisque le backend est toujours actif. En séparant les deux fichiers, cette collision disparaît complètement : le script de curation de tags ne touche jamais à `stats.json`, et le backend ne touche jamais à `tracks.json` en écriture (d'ailleurs monté `:ro` dans le conteneur, voir section 2). Conséquence pratique : grâce à cette séparation totale, le script d'import CSV peut être relancé à tout moment, y compris pendant une partie en cours, sans aucun risque pour les stats runtime — le backend charge `tracks.json` uniquement au démarrage, donc une réédition n'a d'effet qu'au redémarrage suivant.

**Usage dans la sélection** (ajouté le 2026-08-25, retour utilisateur : le compteur s'incrémentait bien mais n'était lu nulle part, donc un même thème ressortait souvent avec les mêmes morceaux d'une partie à l'autre ; poids passé de linéaire à quadratique le 2026-09-13, jugé trop faible) : `RoundService.SelectionnerMorceaux` accepte un paramètre optionnel `Func<string, int> playCount` — quand fourni (c'est le cas dans `GameHub`, via `statsRepository.GetPlayCount`), le tirage n'est plus uniforme mais pondéré, poids `1/(playCount+1)²`. Un morceau jamais joué a donc beaucoup plus de chances de sortir qu'un morceau déjà joué plusieurs fois, sans jamais l'exclure totalement (pondération "douce", pas un anti-répétition strict). `RoundService` reste indépendant de `Blindify.Infrastructure` : il reçoit un délégué plutôt que `IStatsRepository` directement, cohérent avec le fait qu'il reçoit déjà le pool de morceaux en `IReadOnlyList<Track>` plutôt que `ITracksRepository`.

### `stats.json` v2 — statistiques de réponse (V2, socle des sections 12.2/12.3)

En plus de `PlayCount`, chaque entrée gagne deux dictionnaires, remplis en une seule écriture atomique à la fin de chaque round classique (`RoundTimerCoordinator`) ou question bonus (`BonusTimerCoordinator`), via `Blindify.Application.Stats.RoundStatsAggregator` + `IStatsRepository.EnregistrerResultatsRound` (toujours un **delta**, ajouté aux compteurs déjà accumulés) :

```json
{
  "0NJdtoQ3RX5ckBjJlNXhlP": {
    "PlayCount": 3,
    "Reponses": {
      "Qcm:Titre":         { "N": 12, "Correct": 7, "Absent": 2, "TempsCorrectCumulMs": 51000 },
      "TapeReponse:Annee":  { "N": 4,  "Correct": 2, "Absent": 0, "TempsCorrectCumulMs": 19000, "EcartCumul": 9 },
      "Bonus:Qcm:Auteur":  { "N": 3,  "Correct": 1, "Absent": 1, "TempsCorrectCumulMs": 6000 }
    },
    "Confusions": {
      "2AMysGXOe0zzZJMtH3Nizb": { "Presente": 4, "Choisi": 3 }
    }
  }
}
```

- Clé de `Reponses` : `"Mode:Cible"` (ex. `"Qcm:Titre"`), préfixée `"Bonus:"` pour la question bonus. `N` compte les **joueurs exposés au round** (répondants + absents via l'entrée synthétique posée par `TerminerParTimeout`, `RoundAnswer.EstAbsent`), pas les rounds. `EcartCumul` uniquement pour la cible Année (section 12.5).
- Clé de `Confusions` : `TrackId` d'un distracteur QCM. `Presente` = nombre de répondants à qui ce distracteur a été montré (mode Qcm uniquement) ; `Choisi` = nombre l'ayant sélectionné. Exclut la bonne réponse et toute option dont le texte a été modifié par une feinte (`RoundOption.EstFeinte` — mesurerait une confusion de texte, pas de morceau) ; un piège réel (`trapWith`, `RoundOption.EstPiege`) reste compté.
- `Round.Options`/`BonusRound.Options` (`List<RoundOption>`, `Blindify.Domain.Entities`) persistent les options QCM réellement présentées (TrackId, texte affiché pour la cible du round, `EstFeinte`, `EstPiege`) au moment de leur construction — plus jamais recalculées ensuite : corrige au passage un effet de bord où un joueur qui se reconnectait pouvait voir un tirage de feinte différent de celui vu par les autres (les feintes sont probabilistes).
- Ne tient pas compte d'un `ValidateAnswerManually` survenant après l'écriture des stats (limitation assumée — l'override manuel reste rare et la stats sert la curation, pas un compteur temps réel critique).
- Rétrocompatible : une entrée v1 (`PlayCount` seul) se lit avec `Reponses`/`Confusions` vides.
- Jamais lu par le moteur de jeu lui-même (`SelectionnerMorceaux` ne lit que `PlayCount`) — uniquement par le pipeline data (section 12.2/12.3, `data/scripts/report_difficulty.py`/`suggest_traps.py`).

### `flags.json` / `flags_resolutions.json` — signalement en direct (V2, section 12.4)

Même principe de séparation que `tracks.json`/`stats.json`, mais à deux fichiers du côté "runtime" cette fois : `data/flags.json` (liste, backend-writable, `Blindify.Infrastructure.Flags.FlagsRepository`, toujours via `AtomicJsonFile`) et `data/flags_resolutions.json` (dictionnaire, lecture seule, chargé une fois au démarrage comme `tracks.json`, écrit uniquement par `data/scripts/import_flag_resolutions.py`).

```json
// data/flags.json
[
  { "Id": "b3f1…", "TrackId": "0NJdtoQ3RX5ckBjJlNXhlP", "Raison": "MauvaiseVersion",
    "Commentaire": "version live", "Par": "admin", "GameCode": "K4PZ",
    "SerieTags": ["annees-1980"], "Cible": "Titre", "Mode": "Qcm",
    "Horodatage": "2026-10-03T21:14:07Z" }
]

// data/flags_resolutions.json
{ "b3f1…": { "Resolution": "corrige", "Date": "2026-10-05" } }
```

- Un signalement est **ouvert** tant que son `Id` n'apparaît pas dans `flags_resolutions.json`.
- `RaisonSignalementRules.EstBloquante(raison)` (`Blindify.Domain.Enums`) classe les 7 raisons en bloquantes (`PasSaPlace`, `MauvaiseVersion`, `AudioDefectueux`, `MetadonneesFausses`) ou non (`HorsTheme`, `RefrainMalPlace`, `Autre`) — table figée, jamais configurable.
- `IFlagsRepository.ObtenirTrackIdsBloquants()` croise les deux fichiers (bloquant + non résolu) ; `GameHub.ConfigurerPartie`/`RejouerPartie`/`StartBonusRound` pré-remplissent avec ce résultat le `dejaUtilises` déjà utilisé par `RoundService.SelectionnerMorceaux` pour éviter les doublons entre séries — pas de paramètre supplémentaire sur `SelectionnerMorceaux`, `dejaUtilises` EST déjà l'ensemble d'exclusion qu'elle accumule. Réglable par `GameConfig.ExclureMorceauxSignales` (défaut `true`).
- `GameHub.SignalerMorceau(SignalementRequestDto{TrackId, Raison, Commentaire?})` : host **ou** admin authentifié uniquement (`ResoudreSessionHostOuAdmin`, jamais les joueurs), vérifie que `TrackId` a bien été joué dans la session (recherche dans `SeriesList`, round classique ou bonus), déduplique **même morceau + même raison + même partie** (retourne alors l'entrée existante, `DejaSignale=true`). Répond `SignalementResultDto(FlagId, DejaSignale)`.
- Événement `MorceauSignale` : diffusé **uniquement** au host et aux `AdminConnectionIds` (jamais `Clients.Group`, jamais aux joueurs) — simple confirmation visuelle, envoyé même en cas de doublon.
- Réglages admin (Flutter) et panneau host : liste « Morceaux joués dans cette partie », construite côté client (pas un DTO serveur dédié) en accumulant `RoundEnded`/`BonusResult` au fil de la partie — jamais avant le reveal, l'admin est aussi un joueur.
- `docker-compose.yml` monte `flags.json` en `rw` et `flags_resolutions.json` en `ro`, avec le même avertissement opérationnel que `stats.json` (le fichier doit exister, même vide, avant le premier `docker compose up`).
- Pipeline : `export_flags_csv.py [--ouverts] [--raison R] [--ids-only]` → CSV ; `import_flag_resolutions.py CHEMIN.csv [--dry-run]` reporte la colonne `resolution` (`corrige`/`ignore`/`retire`) dans `flags_resolutions.json` uniquement (jamais `tracks.json` ni `flags.json`).

## 5. Modèle de données du jeu

| Entité | Champs clés |
|---|---|
| `GameSession` | id, état (Lobby / EnCours / Terminé), enPause, pauseDémarréeÀ, modeÉquipe (bool), liste de `Series`, joueurs, équipes |
| `Series` | index, liste de `Round` classiques, une `BonusRound` |
| `Player` | **playerId** (stable, généré côté client Flutter et persisté localement — pas le connectionId SignalR), connectionId (mutable, réassocié à chaque (re)connexion), nom, score, teamId (optionnel, si mode équipe), estConnecté (bool) |
| `Team` | id, nom, liste de joueurs, score cumulé (somme des points gagnés par ses membres) |
| `Round` | référence morceau, mode (QCM / TapeReponse / PremiereLettre), débutRound, duréeEnPauseMs, réponses reçues (par joueur : timestamp, réponse, correcte, points) |
| `BonusRound` | référence morceau, 4 paliers de mise (issus de la config de la série), duréeEnPauseMs, mises par joueur, réponses par joueur |

## 6. Cycle de vie d'un round classique

*Le nombre de rounds classiques par série et la durée de la fenêtre de réponse sont des paramètres configurables au niveau de la série (voir section 10).*

1. **Lobby** — attente des joueurs.
2. **Lecture audio** — le host déclenche la lecture, le serveur horodate le début du round (`débutRound`).
3. **Réponses indépendantes** — chaque joueur répond à son propre rythme via `SubmitAnswer(payload)` (pas de verrouillage/buzzer exclusif). Un seul essai par joueur et par round. Le serveur calcule les points au moment de la réception.
4. **Fin du round** — au bout d'une durée fixe, tout joueur n'ayant pas répondu reçoit -5 points.
5. **Résultat + scores** — diffusion à tous les joueurs des scores mis à jour et de la réponse correcte.
6. Retour à l'étape 2 pour le round suivant, jusqu'à épuisement du pool de morceaux de la série.

### Formule de scoring (dégressif selon la vitesse)

```
tempsÉcoulé = (maintenant - débutRound) - duréeEnPauseMs
pointsEnJeu(t) = max(min, max - (tempsÉcoulé / duréeFenêtre) × (max - min))
```

- Réponse juste → le joueur gagne `pointsEnJeu` au moment de sa réponse.
- Réponse fausse → le joueur perd `pointsEnJeu × 0.5` (pénalité réduite par rapport au gain, pour inciter à toujours tenter une réponse plutôt qu'à s'abstenir par prudence — voir note ci-dessous).
- Pas de réponse dans le temps imparti → -5 points fixes.
- `duréeEnPauseMs` neutralise le temps où la partie était en pause, pour ne pas pénaliser injustement.

**Pourquoi une pénalité asymétrique (×0.5) plutôt que symétrique** : ne pas répondre du tout coûte déjà une pénalité fixe (étape 4), donc l'abstention n'est jamais "gratuite" — la question est seulement de savoir à partir de quel niveau de certitude tenter sa chance devient rentable. Avec une pénalité égale au gain (×1), deviner sur un QCM à 4 options sans aucun indice donne une espérance de `-0.5 × pointsEnJeu`, pire qu'une abstention trop dissuasive, donc un joueur hésitant aurait mathématiquement intérêt à ne jamais répondre — à l'encontre de l'esprit "tout le monde participe". Avec ×0.5, ce même guess à l'aveugle reste à espérance négative (`-0.125 × pointsEnJeu`, ce n'est pas un moyen de "rentabiliser le hasard pur"), mais dès que le joueur a éliminé ne serait-ce qu'une option parmi les 4 (3 candidats restants), l'espérance devient nulle, et à 2 candidats restants elle devient nettement positive (`+0.25 × pointsEnJeu`). Le rôle du ×0.5 est donc d'inciter à répondre dès qu'on a un minimum d'indice, pas de rendre le pur hasard profitable, tout en gardant un vrai coût à l'erreur.

**Pénalité d'absence (V2)** : la valeur par défaut était `-5`, ce qui rendait un clic au hasard sur un QCM à 4 options *plus* rentable en espérance (`-0.125 × pointsEnJeu`, souvent moins sévère que -5 en fin de fenêtre quand pointsEnJeu ≈ PointsMin) que l'abstention elle-même — à l'encontre de la logique ci-dessus. Défaut passé à `-2`. `GameHub.ConfigurerPartie` refuse désormais (`HubException`) toute config où `PenaliteAbsenceReponse ≤ -(0.75 × PenaliteMauvaiseReponseRatio - 0.25) × PointsMin` (`IScoringService.EstPenaliteAbsenceEquitable`), pour ne pas pouvoir reproduire ce déséquilibre par une reconfiguration ultérieure.

### Cible de la question (titre, auteur, film ou année)

Chaque round tire une **cible** — `Titre`, `Auteur` ou `Année` (V2) — annoncée au joueur ("trouve le titre" / "trouve l'artiste" / "trouve l'année"). Ajouté suite à un retour de playtest : sans cible explicite, un morceau à plusieurs auteurs (ex. featurings) rendait le mode `TapeReponse` quasi injouable (fallait taper la liste complète) et le mode `PremiereLettre` ambigu (première lettre de quoi ?).

**Tirage pondéré (V2, section 12.5)** — `RoundService.ChoisirCible`, partagée avec la question bonus : tirage pondéré (`GameConfig.PoidsCibleTitre`/`PoidsCibleAuteur`/`PoidsCibleAnnee`, 40/40/20 par défaut) parmi les cibles éligibles pour ce morceau et ce mode. `Titre` reste exclu au-delà de 35 caractères (`TitreVariantes.EstEligibleCommeCible`) ; `Titre`/`Auteur` sont exclus en mode `PremiereLettre` si leur premier caractère normalisé n'est pas une lettre (ex. "50 Cent") ; `Année` n'est éligible que si `Track.Year` est renseigné. **`Année` + Mode `PremiereLettre`** n'a pas de sens (pas de "première lettre" d'un nombre) : quand cette combinaison sort du tirage, `DemarrerRound`/`CreerBonusRound` basculent le mode vers `TapeReponse` après coup, plutôt que d'exclure `Année` du tirage lui-même.

**Exception — morceaux tagués `"disney"`** : la cible est **toujours forcée à `Film`**, jamais tirée au hasard. Ni le titre réel de la chanson ni l'artiste crédité (souvent la voix/l'acteur, ex. "Jason Weaver, Rowan Atkinson, Laura Williams") ne sont des questions jouables pour ce type de contenu — la question naturelle est le film dont est tiré le morceau. Le nom du film est déduit de `Track.Album` (nettoyé des suffixes de bande originale courants côté Spotify — "(Original Motion Picture Soundtrack)", "X Original Soundtrack (French Version)", etc. — voir `FilmNameResolver`), avec priorité à une mention explicite dans le titre lui-même quand elle existe (ex. `"Il vit en toi - Extrait de \"Le roi lion 2\""` → "Le roi lion 2") — plus fiable qu'un album de compilation qui ne nomme aucun film. Cette même règle s'applique à la question bonus (`BonusRoundService.CreerBonusRound`) : un morceau "disney" tiré en bonus demande aussi le film.

- **QCM** : les options affichent uniquement le champ correspondant à la cible (titre, un seul auteur, ou film par option), jamais plusieurs champs concaténés.
- **TapeReponse / PremiereLettre**, cible `Auteur` : le champ `artist` peut lister plusieurs noms séparés par des virgules (ex. `"David Guetta, Tones And I, Teddy Swims"`) — **n'importe lequel** des auteurs listés est accepté comme réponse correcte, pas besoin de tous les citer.
- La cible n'affecte jamais la validation en mode QCM (toujours par sélection d'ID). En revanche elle affecte bien l'écran de révélation : `RoundEnded`/`BonusResult` transportent la cible du round et le film déduit, et l'écran met en avant le film comme réponse quand `cible == Film` (le vrai titre/artiste restent affichés en dessous, à titre de trivia) — pour les cibles Titre/Auteur, le titre et l'artiste complets restent affichés normalement.

### Génération des QCM

- Par défaut : 3 distracteurs tirés dans le même pool genre/tag que le morceau à deviner, en trois replis successifs si le pool se révèle insuffisant.
- **Palier 1 — ancre unique (`QcmGenerator.ChoisirMeilleureAncre`)** : parmi les genres/tags *significatifs* du morceau correct (fréquence ≤ 8 % du catalogue — un genre trop répandu comme "pop" ou une décennie ne suffit pas à lui seul à garantir une cohérence stylistique, seuil relatif plutôt qu'une liste de noms en dur), on choisit celui qui a le plus de candidats éligibles dans le pool, puis **tous** les distracteurs de ce palier doivent partager **cette même ancre**. **Correction (retour utilisateur)** : valider chaque distracteur indépendamment ("partage au moins un tag avec le bon morceau") permettait à un morceau tagué à la fois `pop` et `variete-francaise` de ramener un distracteur anglais via `pop` **et** un distracteur français via `variete-francaise`, sans que les deux distracteurs aient quoi que ce soit en commun entre eux (ex. Fall Out Boy / Patrick Sébastien / Avicii dans le même QCM). Une ancre unique partagée par l'ensemble du groupe évite ça.
- **Barrière francophone** : en plus de l'ancre, tout candidat doit avoir le même statut `variete-francaise` (présent ou absent) que le morceau correct — une ancre générique comme `pop` reste sinon partagée par des morceaux français et anglais à la fois. Retour utilisateur : des titres français apparaissaient comme distracteurs d'un morceau anglais. Cette barrière s'applique aussi au palier 2 ci-dessous.
- **Palier 2 — repli année proche** : si le palier 1 ne suffit pas (aucune ancre significative, ou trop peu de candidats) et que le morceau correct a une année connue, on complète avec les morceaux dont l'année est la plus proche (écart minimal, égalité départagée au hasard) — pas un bucket "même décennie" strict, pour éviter que deux morceaux à un an d'écart (1999/2001) tombent dans des décennies différentes.
- **Palier 3 — pool global** : si toujours insuffisant (thème trop niche, ou série mal configurée), on complète avec des morceaux tirés de tout `tracks.json`, même hors thème et sans contrainte de langue — garantit toujours 4 options valides plutôt qu'un crash ou un round bloqué. Le QCM est alors un peu plus facile dans ce cas limite, ce qui est préférable à l'absence de round.

Trois niveaux de piège indépendants, chacun avec sa propre probabilité (`GameConfig`), pour ne pas que ça tombe trop souvent sur plusieurs parties :

| Niveau | Source du leurre | Cliquer dessus | Config |
|---|---|---|---|
| Piège réel | Un autre vrai morceau du catalogue (`trapWith`) | Compte comme ce morceau réel | `ProbabiliteQcmPiege` (5 % par défaut) |
| Feinte champ croisé | Le champ opposé du morceau correct lui-même (ex. cible Auteur -> affiche son propre titre) | Mauvaise réponse normale contre le distracteur tiré au sort | `ProbabiliteQcmFeinteChamp` (10 % par défaut) |
| Feinte texte inventé | Un texte écrit à la main (`trapTextArtist`), sans rapport avec un morceau réel (ex. Bastille - Pompéi -> "Baptiste") | Mauvaise réponse normale contre le distracteur tiré au sort | `ProbabiliteQcmFeinteTexteArtiste` (5 % par défaut, cible Auteur uniquement) |

Dans les deux cas de feinte, le `TrackId` du distracteur affiché ne change jamais — seul le texte affiché (titre ou artiste) est substitué, le scoring reste celui du distracteur réellement tiré.

### Cible Année (V2, section 12.5)

Pas de morceaux à deviner (contrairement aux autres cibles) : le joueur doit trouver ou approcher l'année de sortie (`Track.Year`).

- **Mode QCM** (`AnneeQcmGenerator`) : 4 années triées, position de la bonne réponse tirée uniformément (0 à 3) puis les distracteurs générés en dessous/au-dessus en conséquence — sans ce tirage de position en amont, la bonne réponse tombe trop souvent au milieu et les joueurs l'apprennent. Écart d'au moins 2 ans entre options consécutives (calculé avec une réserve de budget à chaque option pour ne jamais produire un écart de 1 an en fin de séquence), jamais plus de 10 ans de l'année correcte, jamais dans le futur. La réponse soumise est le texte de l'année choisie (pas un `TrackId`) — comparaison stricte, comme pour toute autre cible en QCM.
- **Mode saisie** (TapeReponse — jamais PremiereLettre, voir ci-dessus) : score dégressif par proximité plutôt que juste/faux, `ScoringService.PointsAnneeApproximative` :

```
écart = |réponse − année|
écart = 0                    → +pointsEnJeu
1 ≤ écart ≤ ToleranceAnnee     → +round(pointsEnJeu × (1 − écart / (ToleranceAnnee + 1)))
écart > ToleranceAnnee       → pénalité de mauvaise réponse habituelle
```

  `SeriesConfig.ToleranceAnnee` (3 par défaut). Une saisie non numérique est traitée comme une mauvaise réponse classique (pas d'écart calculable). `RoundAnswer.EcartAnnee`/`BonusAnswer.EcartAnnee` retiennent l'écart pour les statistiques de réponse (section 12.1) et le reveal (`RoundEnded`/`BonusResult` gagnent un champ `EcartAnnee` par joueur).
- **Question bonus** : pas de dégressivité — la mise est gagnée en entier si `écart ≤ SeriesConfig.ToleranceAnneeBonus` (1 par défaut), perdue en entier sinon (mode QCM : comparaison stricte du texte de l'année).
- **Reveal** : `RoundEndedDto`/`BonusResultDto` gagnent un champ `Annee` (nullable, `Track.Year` tel quel) — sans lui, le host/l'écran public n'auraient aucun moyen de savoir quelle était la bonne année (jamais transmise avant le reveal).
- Pré-requis catalogue : les années de réédition faussent la cible Année — lancer `audit_reissue_years.py`/`apply_reissue_years.py` (section 3bis) sur le catalogue avant de l'activer en partie réelle.

### Joker (V2, section 12.7)

Un joker par joueur et par partie complète (rendu par `RejouerPartie`, un joker par joueur même en mode
équipes). Utilisable uniquement pendant la phase de réponse d'un round classique, tant que le joueur n'a pas
répondu et que la partie n'est pas en pause — jamais en question bonus. Aucune pénalité en points, le chrono
continue. Réduit le champ des possibles, ne donne jamais directement la réponse.

Effet selon le mode/la cible du round (`JokerService.CalculerIndice`, `Blindify.Application`) :

| Mode | Cible | Effet |
|---|---|---|
| Qcm | toutes (dont Année) | 50/50 : deux des trois mauvaises options retirées au hasard, jamais la bonne |
| PremiereLettre | Titre / Auteur | Il ne reste que 4 tuiles (dont la bonne) sur les 26 |
| TapeReponse | Titre / Auteur | Pochette floutée côté serveur + structure du texte (lettres masquées, espaces/ponctuation/chiffres préservés) |
| TapeReponse | Film | Structure du texte seule |
| TapeReponse | Annee | La décennie de sortie est révélée |

Contrat (`GameHub.UtiliserJoker(roundId)`, joueurs uniquement) : vérifie round classique courant, pas de
réponse déjà donnée, joker disponible, partie non en pause (toute la validation vit dans
`RoundService.UtiliserJoker`, qui retourne `null` sur toute condition invalide — `GameHub` traduit `null` en
`HubException`, même philosophie que `SoumettreReponse`). Renvoie à l'appelant seul un `JokerIndiceDto
{ OptionsRetirees?, TuilesRestantes?, Structure?, Decennie?, CoverUrl? }`. Diffuse à tout le groupe
l'événement `JokerUtilise{ PlayerId }`, qui ne révèle rien (ni l'effet ni la cible). L'indice calculé est
persisté sur `Round.JokerIndicesParJoueur` (borné à la durée de vie du round) : une reconnexion pendant ce
round rejoue exactement le même indice plutôt qu'un nouveau tirage — `JoinGameResultDto`/
`EtatCourantConnexionDto` portent `JokerDisponible` (bool, par partie), et `RoundStartedForPlayersDto` porte
l'indice déjà obtenu pour le round en cours.

Pochette floutée : `GET /api/joker/cover/{jeton}` (`SixLabors.ImageSharp`, flou gaussien serveur, rayon 40),
jeton à usage unique émis par `IJokerCoverTokenStore` et lié au morceau du round — `/files` reste réservé au
host, l'audio et les pochettes en clair ne transitent jamais vers les joueurs avant le reveal.

Domaine et stats : `Player.JokerUtilise` (remis à `false` par `RejouerPartie`), `RoundAnswer.AvecJoker` — les
réponses avec joker sont exclues de `Reponses`/`Confusions` dans `stats.json` (section 4), pour ne pas fausser
la mesure de difficulté réelle du morceau.

Flutter : petit bouton rond (`JokerButton`) dans la barre du haut à côté du timer, contour moutarde si
disponible, grisé/barré si utilisé, masqué en bonus et une fois répondu. Appui long (~0,6 s, anneau qui se
remplit) déclenche l'effet sur place, pas de boîte de dialogue. Mention « 1 joker pour la partie » au lobby.

## 7. Question bonus (fin de série)

Mécanique en deux phases, mise choisie **à l'aveugle** avant de découvrir la question. *La durée de la phase mise et celle de la phase question sont configurables (voir section 10).*

1. **Phase mise** — le serveur annonce les 4 paliers de la série courante (safe / moyen / moyen+ / risqué, définis dans une table de config par série, croissants jusqu'à 3000 pts en fin de partie). Chaque joueur choisit un palier via `SelectStake(index)`. Délai limite (~15s) : pas de choix → palier "safe" appliqué par défaut.
2. **Phase question** — une fois tous les choix reçus (ou le délai passé), le morceau est révélé et un timer fixe démarre, **sans dégressivité**. Le morceau est joué **ralenti** par défaut pour complexifier la tâche (`playbackRate` réduit côté lecteur audio du host, ex. 0.8) — paramètre `ralentissementBonusActivé` (bool) désactivable, avec un facteur configurable. Un seul essai par joueur. Pas de réponse dans le temps imparti → traité comme une réponse fausse (perte de la mise).
3. **Résultat** — réponse juste : `+mise` ; réponse fausse ou absence de réponse : `-mise`.
4. **Tableau général** — affiché **au moins une fois par partie** (pas systématiquement à chaque série). Par défaut, déclenché automatiquement après la série médiane (`⌈nombreDeSéries / 2⌉`), et le host peut aussi le déclencher manuellement à tout moment via une commande dédiée (`ShowLeaderboard()`).

**Enchaînement côté host (`host/`)** — comportement de la page web, pas une règle du contrat serveur : une fois la série classique **courante** épuisée, le host déclenche automatiquement `StartBonusRound()` (au lieu d'attendre une intervention) pour la question bonus de **cette** série. Après réception de `BonusResult`, deux cas : s'il reste une série suivante dans la partie, le host affiche un compte à rebours puis enchaîne automatiquement sur son premier round (`NextRound()` + `StartRound()`, bouton "Série suivante maintenant" disponible pour ne pas attendre) ; sinon (dernière série), le host affiche un compte à rebours et déclenche automatiquement `EndGame()` (bouton "Terminer maintenant"). Le host garde la main pour interrompre cet enchaînement (pause, tableau général) à tout moment.

Table de config des paliers par série — `SeriesConfig.PaliersDeMise` est calculé côté serveur par
`SeriesPlanner.PaliersPourSerie(indexSerie, GameConfig.FacteurProgressionPaliers)` à chaque
`ConfigurerPartie` (jamais fourni par le client, voir section 11). Progression géométrique à partir de la
série de base `[10, 20, 30, 50]`, `facteur = FacteurProgressionPaliers ^ indexSerie`.

**V2 — ratio constant plutôt qu'une cible fixe à 3000 pts** : la version précédente calculait la raison
géométrique pour atteindre exactement 3000 pts à la *dernière* série de la partie, donc la progression
dépendait du nombre de séries choisi — avec seulement 2 séries, ça donnait un premier bonus à 100 pts max
et un second à 3000 pts, un saut jugé trop brusque (retour utilisateur). `FacteurProgressionPaliers`
(défaut `1.6`) est désormais une constante de `GameConfig`, indépendante du nombre de séries. Exemple avec
le défaut 1.6 :

```
Série 1  (index 0) : [10, 20, 30, 50]
Série 2  (index 1) : [16, 32, 48, 80]
Série 5  (index 4) : [66, 131, 197, 328]
Série 10 (index 9) : [687, 1374, 2062, 3436]
```

### Mode "course" (retour utilisateur)

Variante réservée au mode Qcm de la question bonus (tiré aléatoirement parmi Qcm/TapeReponse/PremiereLettre, voir plus haut) : probabilité configurable `GameConfig.ProbabiliteBonusCourse` (50 % par défaut) qu'un round Qcm devienne une "course" (`BonusRound.EstCourse`), jamais appliquée aux deux autres modes — répondre à voix haute ou par écrit n'a pas de sens pour départager qui a "buzzé" en premier, alors que les options Qcm restent le même clic qu'un round normal, seul l'ordre d'arrivée compte côté serveur.

- Le **premier joueur à répondre** — juste ou faux — décide seul du sort de sa mise (`+mise` si correct, `-mise` sinon). La phase question se termine alors immédiatement pour tout le monde (pas d'attente du timer complet).
- Tant qu'aucune réponse n'est enregistrée, les autres joueurs ayant misé ne sont **ni gagnants ni perdants** : leur mise leur reste acquise (pas de perte), et ils n'apparaissent pas dans `BonusResult.resultats`. Toute réponse reçue après la première est silencieusement ignorée côté serveur.
- Si **personne** ne répond avant l'expiration du timer, comportement inchangé : tout le monde perd sa mise.
- Révélé aux clients seulement dans `BonusQuestionStarted` (champ `estCourse`) — jamais pendant la phase de mise à l'aveugle (`BonusStakeOptions`), pour ne pas influencer le choix du palier avant même de savoir que ce sera un Qcm.

## 8. Mode équipes (optionnel)

Activable via `modeÉquipe` sur `GameSession`. Chaque joueur est rattaché à une `Team` (`teamId`), formées au moment du lobby.

- Chaque joueur continue de répondre individuellement (même mécanique de round : réponses indépendantes, un seul essai par joueur et par round), mais les points gagnés ou perdus sont crédités/débités du score de son **équipe** plutôt que d'un score individuel.
- Pour la question bonus, chaque joueur choisit toujours sa propre mise et répond individuellement — le gain ou la perte s'applique au score d'équipe. Ça garde le côté "tout le monde participe" plutôt que de désigner un seul joueur par équipe pour la mise.
- `ScoreUpdate` et le tableau général affichent alors le classement par équipe (avec, en détail si besoin, la contribution de chaque membre).
- Les égalités entre équipes suivent la même règle que pour les joueurs individuels (acceptées, pas de départage).

## 9. Pause de partie

- Le host peut mettre la partie en pause à tout moment (`PauseGame()` / `ResumeGame()`), y compris pendant un round classique ou une question bonus.
- Diffusion à tous les clients : les apps joueurs désactivent la saisie, le host met en pause l'audio nativement (balise `<audio>` du navigateur).
- La reprise se fait **là où la lecture s'était arrêtée** (position audio conservée côté host, pas de redémarrage du morceau).
- `duréeEnPauseMs` est incrémenté sur la durée de la pause, pour neutraliser son effet dans le calcul des points (voir formule section 6).
- Filet de sécurité serveur : toute soumission (`SubmitAnswer` / `SelectStake`) reçue pendant `enPause = true` est rejetée.

## 10. Contrat SignalR (`GameHub`)

### Méthodes déclenchées par le host

| Méthode | Effet |
|---|---|
| `CreateGame(modeÉquipe, nomsÉquipes?)` | Crée le lobby (code, `hostSecret`) — volontairement minimal depuis le retour utilisateur du 2026-08-24 (voir juste en dessous), ne configure aucune série. `nomsÉquipes` : une `Team` créée par nom fourni, uniquement si `modeÉquipe` actif (ignoré sinon). Retourne les équipes créées (id + nom) et un `hostSecret` opaque (voir `RejoinAsHost`) |
| `ConfigurerPartie(séries)` | Configure (ou reconfigure entièrement) le blindtest — sélectionne le pool de morceaux pour chaque série demandée. Séparé de `CreateGame` depuis le retour utilisateur du 2026-08-24 : auparavant, la configuration complète devait être soumise AVANT que le lobby n'existe, donc toute erreur de config forçait à recréer toute la partie — les joueurs devaient alors quitter et rouvrir l'app Flutter faute d'un moyen de rejoindre une nouvelle partie sans redémarrage complet. Rappelable autant de fois que nécessaire tant que la partie est encore `Lobby` (refusé sinon) ; chaque appel remplace entièrement la configuration précédente. `StartRound`/`StartBonusRound`/`AnnoncerSerieCourante` refusent tant qu'aucune série n'a été configurée |
| `RejoinAsHost(code, hostSecret)` | Resynchronise le host après un refresh/crash de l'onglet : renvoie l'état courant complet (morceau en cours, mode, position audio théorique calculée depuis `débutRound`/`duréeEnPauseMs`, `enPause`) pour reprendre la lecture au bon endroit sans redémarrer le morceau. `hostSecret` — généré à `CreateGame`, distinct du code de partie (public, connu de tous les joueurs) — est exigé pour empêcher n'importe quel client du réseau local connaissant seulement le code de usurper le rôle host (pause, override, fin de partie) |
| `AnnoncerSerieCourante()` | Diffuse l'annonce de la série en cours (`SerieAnnoncee`, index + tags) à tous les clients (host, écran public, joueurs) — appelé par le host au même moment où il affichait déjà cet écran localement, avant le premier round de chaque série (y compris la première) |
| `StartRound()` | Démarre un round classique, horodate `débutRound` |
| `StartBonusRound()` | Démarre la phase de mise d'une question bonus |
| `ShowLeaderboard()` | Déclenche manuellement l'affichage du tableau général |
| `PauseGame()` / `ResumeGame()` | Gèle/reprend la partie en cours |
| `ValidateAnswerManually(playerId, correct)` | Override manuel pour les réponses texte ambiguës |
| `NextRound()` | Passe au round suivant |
| `EndGame()` | Termine la partie |
| `RejouerPartie()` | Uniquement si la partie est `Terminée` : relance une nouvelle manche avec le même code et les mêmes joueurs — mêmes configs/modes de série qu'à la création mais nouvelle sélection de morceaux, scores remis à zéro, session repassée en `Lobby`. Évite aux joueurs de retaper le code entre deux manches. |
| `AuthenticateAdmin(password)` *(host ou joueur)* | Authentifie la connexion courante comme admin (V2, `Admin:RemoteControlPassword`, distinct du `hostSecret`) — voir "Contrôle admin depuis l'app Flutter" section 2. Débloque `PauseGame`/`ResumeGame`/`ShowLeaderboard`/`EndGame`/`SignalerMorceau` en plus du host, jamais les actions ci-dessus qui restent exclusives au host web |
| `SignalerMorceau(SignalementRequestDto)` *(host ou admin)* | V2, section 12.4 — `{ trackId, raison, commentaire? }`. Vérifie que `trackId` a bien été joué dans la session, déduplique même morceau + même raison + même partie. Retourne `{ flagId, dejaSignale }`, diffuse `MorceauSignale` au host et aux admins seulement |

### Méthodes déclenchées par les joueurs

RoundId (V2) : `SubmitAnswer`/`SelectStake`/`SubmitBonusAnswer` reprennent le `RoundId`/`BonusRound.Id` reçu dans l'événement correspondant (`RoundStarted`/`BonusStakeOptions`/`BonusQuestionStarted`) — le serveur ignore silencieusement (même chemin qu'une soumission en double) toute soumission dont le `RoundId` ne correspond plus au round/à la phase bonus courante, ex. réponse arrivée en retard après une reconnexion pendant que le round suivant a déjà démarré.

| Méthode | Effet |
|---|---|
| `JoinGame(code, nom, playerId)` | Rejoint la partie, ou s'y réassocie explicitement. `playerId` est un identifiant stable généré et persisté côté client Flutter (pas le `connectionId` SignalR, qui change à chaque reconnexion). Si ce `playerId` existe déjà dans la partie, le serveur réassocie simplement le nouveau `connectionId` au `Player` existant et renvoie son état (score, équipe, phase en cours) au lieu de créer un nouveau joueur. Depuis V2, ce rattachement se fait aussi automatiquement à la connexion (voir "Reconnexion automatique" ci-dessous) — `JoinGame` reste nécessaire pour le tout premier join (poser le pseudo) et sert de filet de sécurité si l'automatique échoue |
| `JoinTeam(teamId)` | Rejoint (ou change d')équipe — autorisé à tout moment, pas seulement au lobby. La liste des équipes disponibles est renvoyée par `JoinGame`, pas besoin d'appel séparé pour les découvrir |
| `SubmitAnswer(roundId, payload)` | Soumet une réponse (round classique) |
| `SelectStake(roundId, index)` | Choisit un palier de mise (phase 1 bonus) |
| `SubmitBonusAnswer(roundId, payload)` | Soumet une réponse (phase 2 bonus) |
| `UtiliserJoker(roundId)` | V2, section 12.7 — active le joker sur le round classique en cours, renvoie un `JokerIndiceDto` à l'appelant seul (jamais en bonus, jamais après réponse, jamais en pause, jamais deux fois) |

### Reconnexion automatique (V2)

`GameHub.OnConnectedAsync` — le client Flutter ouvre le hub avec `/hubs/game?code=XXXX&playerId=...` dès que le code de partie est connu (avant : pas de query string). SignalR réutilise cette même URL à chaque reconnexion transport (`withAutomaticReconnect`), donc ce handler s'exécute à chaque (re)connexion :

- Si `code`/`playerId` sont présents et correspondent à un `Player` déjà enregistré dans cette partie : réassocie le nouveau `ConnectionId` (même effet que la réassociation faite par `JoinGame`), rattache la connexion au groupe SignalR, puis envoie à l'appelant un événement `EtatCourant` (score, équipe, phase de jeu en cours — même contenu que ce que `JoinGame` renvoie) et diffuse `PlayerReconnected` aux autres. Le joueur peut ainsi répondre immédiatement au round/à la phase bonus en cours, sans jamais rappeler `JoinGame` lui-même.
- Ne concerne jamais le host : `RejoinAsHost` reste le seul chemin de resynchronisation host, authentifié par `hostSecret` (une simple query string non authentifiée serait insuffisante pour ce rôle).
- **Délai de grâce** (`GameConfig.DelaiGraceDeconnexionMs`, 5000 ms par défaut) : `OnDisconnectedAsync` attend ce délai avant de diffuser `PlayerDisconnected`, en revérifiant qu'aucune reconnexion n'a entre-temps réassocié un nouveau `ConnectionId` au joueur — une micro-coupure (verrouillage d'écran, changement de réseau) ne fait donc pas clignoter l'indicateur de connexion côté host.

### Événements diffusés par le serveur

| Événement | Contenu |
|---|---|
| `PlayerJoined` | Infos du joueur (nouveau joueur) |
| `PlayerReconnected` / `PlayerDisconnected` | Changement d'état `estConnecté` d'un joueur existant (perte réseau, reconnexion) — `PlayerDisconnected` différé du délai de grâce, voir "Reconnexion automatique" |
| `PlayerTeamChanged` | Un joueur a rejoint (ou changé d')équipe |
| `EtatCourant` *(V2, joueur uniquement)* | Envoyé par `OnConnectedAsync` lors d'une reconnexion automatique — score, équipe, phase de jeu en cours (même contenu que le `EtatCourant` renvoyé par `JoinGame`), voir "Reconnexion automatique" |
| `SerieAnnoncee` | Index de la série qui commence + ses tags (thème) — voir `AnnoncerSerieCourante()`. Retour utilisateur du 2026-08-24 : auparavant affiché uniquement côté host/écran public, jamais diffusé aux joueurs |
| `RoundStarted` | `RoundId` (V2, à renvoyer dans `SubmitAnswer`), morceau (mode-dépendant), mode, cible (Titre/Auteur/Film/Année — voir section 6), URL audio + `refrainStartMs` (host uniquement — mémorisé côté host, appliqué au moment du `RoundEnded`, pas pendant la découverte). Options QCM (si applicable) incluent un champ `Film` par option, affiché à la place de titre/auteur quand la cible est Film ; `AnneeOptions` (V2) à la place de `QcmOptions` si la cible est Année. Version joueurs : `JokerIndice` (V2, section 12.7), toujours `null` sauf reconstruction après reconnexion si ce joueur avait déjà utilisé son joker sur ce round |
| `JokerUtilise` *(V2, section 12.7)* | `{ playerId }` — diffusé à tout le groupe quand un joueur active son joker, ne révèle jamais l'effet ni la cible |
| `ScoreUpdate` | Scores à jour de tous les joueurs |
| `RoundEnded` | Réponse correcte (titre + artiste), détail des points de chacun (`EcartAnnee` par joueur si la cible était Année, V2), cible du round, film déduit et année réelle (`Cible`/`Film`/`Annee`) — l'écran de révélation met le film ou l'année en avant selon `Cible` |
| `BonusStakeOptions` | `RoundId` (V2, identité du `BonusRound`, stable entre les deux phases), les 4 paliers de la série courante |
| `BonusQuestionStarted` | Morceau révélé, timer fixe démarré + `refrainStartMs` (host uniquement — appliqué au `BonusResult`, pas pendant la devinette). Version joueurs inclut la cible (Titre/Film/Année). `estCourse` (Qcm uniquement, voir section 7) : révélé ici, jamais avant. `AnneeOptions` (V2) : voir `RoundStarted` |
| `BonusResult` | Résultat de chaque joueur (mise gagnée/perdue, `EcartAnnee` si pertinent), cible, film déduit et année réelle (mêmes champs que `RoundEnded`). `estCourse` : si vrai, seul le premier répondant apparaît dans `resultats` — voir section 7 |
| `LeaderboardShown` | Classement général, diffusé en fin de série |
| `GamePaused` / `GameResumed` | État de pause |
| `GameEnded` | Scores finaux (`score`, même forme que `ScoreUpdate`) + `titres` (V2, section 12.6) — voir "Titres de fin de partie" |
| `GameRestarted` | Diffusé après `RejouerPartie()` — ramène tous les clients à l'écran du lobby (même code, mêmes joueurs, scores à zéro) |
| `MorceauSignale` *(host + admins uniquement, V2)* | `{ trackId, titre, artiste, raison }` — confirmation visuelle après `SignalerMorceau`, jamais diffusé aux joueurs |

## 11. Paramètres de configuration

Tous ces éléments sont des paramètres de partie/série, pas des valeurs figées dans le code :

| Paramètre | Niveau | Notes |
|---|---|---|
| Nombre de rounds classiques par série | Série | Peut varier d'une série à l'autre au sein d'une même partie |
| Durée de la fenêtre de réponse (round classique) | Série ou global | Utilisée dans la formule de scoring dégressif |
| Durée de la phase mise (question bonus) | Série | Délai avant application du palier "safe" par défaut |
| Durée de la phase question (question bonus) | Série | Pas de dégressivité, juste une limite dure |
| Paliers de mise (4 valeurs) | Série | Calculés par le serveur, voir section 7 |
| Facteur de progression des paliers (`FacteurProgressionPaliers`) | Global | 1.6 par défaut (V2) — ratio géométrique constant entre séries, voir section 7 |
| Pénalité d'absence de réponse (`PenaliteAbsenceReponse`) | Série | -2 par défaut (V2, était -5) — `ConfigurerPartie` rejette une valeur trop sévère par rapport à `PenaliteMauvaiseReponseRatio`/`PointsMin`, voir section 6 |
| Probabilité d'un QCM piège réel (`trapWith`) | Global | 5 % par défaut, ajustable |
| Probabilité d'une feinte champ croisé | Global | 10 % par défaut, ajustable |
| Probabilité d'une feinte texte inventé (`trapTextArtist`) | Global | 5 % par défaut, ajustable, cible Auteur uniquement |
| Seuil de tolérance Levenshtein (ratio) | Global | Voir recommandation ci-dessous |
| Longueur minimale pour le ratio (`LongueurMinimalePourTolerance`) | Global | 10 caractères par défaut — en dessous, tolérance fixe (voir recommandation ci-dessous) |
| Longueur minimale pour toute tolérance (`LongueurMinimalePourToleranceFixe`) | Global | 4 caractères par défaut — en dessous, réponse exacte exigée |
| Tolérance fixe zone intermédiaire (`ToleranceFixeReponseCourte`) | Global | 2 caractères d'écart par défaut, entre les deux seuils de longueur ci-dessus |
| Probabilité de mode "course" (question bonus, Qcm uniquement) | Global | 50 % par défaut, voir section 7 |
| Ralentissement audio (question bonus) | Global | Activé/désactivé + facteur de ralentissement (0.65 par défaut), voir section 7 |
| Affichage du tableau général | Partie | Au moins une fois par partie, par défaut après la série médiane, déclenchable aussi manuellement par le host |
| Mode équipe | Partie | Activé/désactivé, voir section 8 |
| Poids de tirage des cibles (`PoidsCibleTitre`/`PoidsCibleAuteur`/`PoidsCibleAnnee`) | Global | 40/40/20 par défaut (V2) — voir section 6 |
| Tolérance Année, mode saisie (`ToleranceAnnee`) | Série | 3 ans par défaut (V2) — dégressivité par proximité, voir section 6 |
| Tolérance Année, question bonus (`ToleranceAnneeBonus`) | Série | 1 an par défaut (V2) — tout ou rien, pas de dégressivité |
| Délai de grâce avant `PlayerDisconnected` (`DelaiGraceDeconnexionMs`) | Global | 5000 ms par défaut (V2) — voir section 10, "Reconnexion automatique" |
| Exclusion des morceaux signalés (`ExclureMorceauxSignales`) | Global | `true` par défaut (V2) — voir section 4, "flags.json" |

Concrètement, ça se traduit par une classe `SeriesConfig` (nombre de rounds, durée réponse, paliers de mise, durées des phases bonus) instanciée par série au moment de `ConfigurerPartie`, plutôt que des constantes fixes dans le code.

### Seuil de tolérance Levenshtein — recommandation

Aucune valeur universelle ne convient à tous les titres (un titre de 3 caractères et un de 30 caractères n'ont pas la même tolérance à l'erreur). Je recommande un seuil proportionnel à la longueur du texte normalisé (minuscules, accents retirés, ponctuation ignorée) :

```
seuil = max(1, floor(longueur(texteNormalisé) × 0.2))
```

Concrètement : ~20 % de caractères d'écart tolérés, avec un minimum de 1. Ça reste un point de départ — à ajuster après quelques parties de test si ça se montre trop laxiste (des réponses clairement fausses validées) ou trop strict (des réponses correctes rejetées pour une faute de frappe).

**Correction (retour utilisateur)** : ce seuil minimal d'1 caractère rendait presque n'importe quelle réponse acceptable sur un texte très court (ex. "Xo" accepté pour "Go", distance 1 ≤ seuil 1). En dessous de `LongueurMinimalePourTolerance` (10 caractères par défaut, texte normalisé), la tolérance était désactivée : seule une réponse strictement exacte était acceptée. Au-delà, la formule ci-dessus s'applique normalement.

**Deuxième correction (retour utilisateur)** : ce "tout ou rien" en dessous de 10 caractères était devenu trop strict dans l'autre sens — "Hayley" tapé pour "Halsey" (6 caractères, distance 2) comptait faux. Trois zones désormais, selon la longueur du texte attendu normalisé :

```
longueur < LongueurMinimalePourToleranceFixe (4)        -> réponse exacte exigée
LongueurMinimalePourToleranceFixe <= longueur < LongueurMinimalePourTolerance (10)
                                                          -> distance <= ToleranceFixeReponseCourte (2)
longueur >= LongueurMinimalePourTolerance (10)           -> seuil = max(1, floor(longueur × 0.2)) [formule ci-dessus]
```

La zone intermédiaire évite à la fois le laxisme sur un texte à 2-3 lettres (toujours réponse exacte) et la sévérité excessive sur un nom à 5-9 lettres (2 caractères d'écart tolérés, indépendamment du ratio qui donnerait un seuil ridiculement bas).

### Outil de curation des tags

Plutôt qu'une interface web dédiée (temps de dev pour un usage ponctuel), un aller-retour par tableur — implémenté dans `data/scripts/` suite au retour utilisateur du 2026-08-24 (morceaux "variété française" mal tagués "electro" par le bucketing automatique, voir section 3 point 3) :

1. `python data/scripts/export_tags_csv.py [--tag TAG]` : `tracks.json` → CSV avec colonnes `id`, `title`, `artist`, `year`, `genres` (lecture seule), `tags` (éditable) — `--tag` limite l'export à une seule catégorie à relire (ex. `--tag electro`).
2. Édition de la colonne `tags` au tableur (Excel — encodage `utf-8-sig`) — tri, filtre, remplissage par glisser-copier pour les morceaux d'un même thème.
3. `python data/scripts/import_tags_csv.py CHEMIN.csv [--dry-run]` : réinjecte uniquement la colonne `tags` par `id` (les autres colonnes du CSV sont ignorées) — un CSV filtré par `--tag` peut être réimporté tel quel sans toucher au reste du catalogue.

Plus rapide à mettre en place qu'une UI web, et plus confortable pour l'édition en masse de ~1000 lignes.

### Pipeline difficulté / pièges / signalements (V2, sections 12.2/12.3/12.4)

Même principe que l'outil de curation des tags ci-dessus — le script propose un CSV, l'organisateur tranche au tableur, un second script réinjecte. `data/scripts/stats_common.py` (module partagé, jamais exécuté seul) lit `stats.json` v2 (`Reponses`/`Confusions`) et calcule les taux par morceau, pour éviter de dupliquer cette lecture entre les scripts ci-dessous.

**Difficulté mesurée** (12.2) — une information pour l'organisateur, **jamais** lue par le moteur de jeu (`SelectionnerMorceaux`, le choix de la cible et la génération QCM ignorent tout ce qui suit) :

1. `python data/scripts/report_difficulty.py [--tag TAG] [--sans-bonus] [--min-n 5] [--seuil-facile 0.70] [--seuil-difficile 0.30]` : `stats.json` + `tracks.json` (lecture seule) → CSV trié du plus dur au plus facile. Colonnes : `id, title, artist, year, tags, n, tauxReussite, tauxTitre, tauxAuteur, tauxAnnee, tauxAbsence, tempsMoyenBonneReponseS, niveau` — `niveau` vaut `facile`/`moyen`/`difficile` selon les seuils, ou `insuffisant` si `n < --min-n`. `--sans-bonus` exclut les réponses données en question bonus (audio ralenti, donc biaisées).
2. `export_tags_csv.py` gagne au passage une colonne `tauxReussite` (même module `stats_common.py`), lecture seule pendant la curation des tags — ignorée par `import_tags_csv.py`.

**Pièges détectés automatiquement** (12.3) — repère les paires réellement confondues par les joueurs, pour alimenter `trapWith` :

1. `python data/scripts/suggest_traps.py [--presente-min 3] [--choisi-min 2] [--taux-min 0.4]` : lit `Confusions` dans `stats.json`, candidate une paire (X, Y) si `Presente >= --presente-min`, `Choisi >= --choisi-min` et `Choisi / Presente >= --taux-min` (trois seuils ajustables — volume de parties familiales faible). Exclut les paires déjà dans `trapWith`. CSV : `id, title, artist, confonduId, confonduTitle, confonduArtist, presente, choisi, taux, symetrique, valider` (`symetrique=oui` si la confusion existe aussi dans l'autre sens) — colonne `valider` vide, à remplir `x` pour les paires retenues.
2. `python data/scripts/import_traps_csv.py CHEMIN.csv [--dry-run]` : pour chaque ligne `valider=x`, ajoute `confonduId` au `trapWith` de `id` (et l'inverse si `symetrique=oui`). N'enlève jamais rien, idempotent, écriture atomique de `tracks.json`.

**Signalement en direct** (12.4) — traite après coup les morceaux signalés en partie (voir section 4, "flags.json") :

1. `python data/scripts/export_flags_csv.py [--ouverts] [--raison RAISON] [--ids-only]` : `flags.json` + `flags_resolutions.json` (lecture seule) → CSV `flagId, date, trackId, title, artist, youtubeUrl, raison, commentaire, serieTags, resolution`. `--ouverts` limite aux signalements pas encore résolus ; `--ids-only` sort une seule colonne `id` (trackId dédoublonné), directement réutilisable par `redownload_tracks.py --csv`.
2. Remplissage de la colonne `resolution` (`corrige`/`ignore`/`retire`) au tableur.
3. `python data/scripts/import_flag_resolutions.py CHEMIN.csv [--dry-run]` : reporte cette colonne dans `flags_resolutions.json` uniquement (jamais `tracks.json` ni `flags.json`) — écriture atomique, idempotent, une ligne à `resolution` vide (signalement encore ouvert) est ignorée.

### Égalités en fin de partie

Décision : les égalités sont acceptées telles quelles, pas de mécanisme de départage. Le classement final peut afficher plusieurs joueurs ex-æquo.

### Titres de fin de partie (V2, section 12.6)

`Blindify.Application.Awards.TitresService.CalculerTitres(GameSession, Func<string, Track?>)` — statique et pur, zéro dépendance ASP.NET Core, appelé une seule fois par `GameHub.EndGame()`. Rejoue `Reponses`/`Mises` de toutes les séries pour calculer 10 statistiques par joueur, puis attribue les titres suivants (seuil minimal entre parenthèses) :

| Code | Titre | Règle | Seuil |
|---|---|---|---|
| `ECLAIR` | Éclair | Plus petit temps moyen sur les bonnes réponses (round classique) | 3 bonnes réponses |
| `SNIPER` | Sniper | Meilleur taux de bonnes réponses parmi les réponses données (round classique) | Avoir répondu à 50 % des rounds |
| `SPECIALISTE` | Spécialiste {tag} | Meilleur taux sur un tag (décennie/genre) — le tag retenu est celui où le joueur brille le plus | 3 rounds sur le tag, taux ≥ 75 %, tag présent sur moins de 80 % des rounds classiques de la partie |
| `HORLOGE` | Horloge suisse | Plus petit écart moyen en cible Année (round classique + bonus) | 2 réponses en cible Année |
| `ROI_BONUS` | Roi du bonus | Plus de points nets gagnés en questions bonus | Gain net > 0 |
| `KAMIKAZE` | Kamikaze | Plus grande part de mises au palier maximum (index 3) | 2 mises bonus |
| `PRUDENT` | Tortue prudente | Plus de mises au palier safe (index 0) + d'abstentions bonus | 3 occurrences |
| `REMONTADA` | Remontada | Plus forte progression de classement entre la mi-partie (rejouée à partir des événements) et le classement final (`Player.Score`) | 2 places gagnées |
| `PANNEAU` | Tombé dans le panneau | Plus de réponses sur une option piège (`trapWith`) ou une feinte (round classique + bonus) | 2 occurrences |
| `JOKER_GACHE` | Joker gâché | A utilisé son joker et s'est quand même trompé (V2, section 12.7) — le plus tôt dans la partie en cas d'égalité | Au moins une occurrence |
| `CHAT_NOIR` | Chat noir | Plus de points perdus en mauvaises réponses (non absentes, round classique + bonus) | Somme négative |
| `FIDELE` | Présent jusqu'au bout | Titre de repli | Toujours éligible |

Attribution : pour chaque titre, les joueurs à égalité sur la meilleure valeur mesurée le partagent tous. Les titres sont ensuite distribués du plus rare (le moins de gagnants) au plus courant — égalité de rareté départagée par l'ordre du tableau ci-dessus — avec un plafond de **2 titres par joueur** : un joueur déjà à son plafond est retiré du groupe de gagnants d'un titre (les autres gagnants du même titre le reçoivent quand même) ; si le seul gagnant d'un titre est plafonné, ce titre n'est décerné à personne plutôt que d'être reporté sur un autre joueur. Tout joueur qui termine sans le moindre titre reçoit `FIDELE` (entrée partagée).

Le DTO `TitreDto { Code, Libelle, Description, PlayerIds[] }` est identique pour host et joueurs — `Description` contient déjà la valeur mesurée en toutes lettres (ex. "2,4 s en moyenne"), aucun secret à protéger contrairement aux DTOs de round. En mode équipe, les titres restent **individuels** (les réponses le sont déjà, voir section 8) — aucune agrégation par équipe.

Affichage : côté joueur (`ended_screen.dart`), les titres apparaissent sous le podium, ceux du joueur courant mis en évidence. Côté host/écran public, un défilement séquentiel (~4 s/titre, `main.js:demarrerDefilementTitres`) démarre juste après `GameEnded` ; le bouton "Titre suivant" permet au host d'avancer immédiatement (interruption du minuteur en cours).
