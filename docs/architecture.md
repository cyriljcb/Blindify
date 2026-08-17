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

Seul le **backend** est dockerisé. Le frontend (app Flutter et page web du host) n'est jamais containerisé — ce sont des clients qui tournent nativement (téléphone / navigateur) et consomment le backend via le réseau. La dockerisation ne change donc rien au modèle de jeu ni au contrat SignalR ; c'est purement une question de déploiement.

Points d'attention :

- **Les données ne sont jamais copiées dans l'image** : `tracks.json`, les fichiers audio et les covers sont montés en volume depuis le HDD 2 To du Pi, pas *baked* dans l'image Docker. Ça permet d'ajouter des morceaux sans reconstruire l'image.
- **Port SignalR exposé sur le LAN** : publication du port du conteneur vers le réseau local (ex. `-p 5000:8080`), pas besoin de tunnel externe (ngrok, utilisé sur l'ancien Blindify) puisque l'usage reste local.
- **Politique de redémarrage** : `restart: unless-stopped` pour survivre à un reboot du Pi, cohérent avec le reste du homelab.
- **CORS** : le backend doit autoriser les origines des clients LAN (IP du PC host, éventuellement de l'app Flutter si elle passe par du HTTP avant l'upgrade WebSocket) — pas besoin d'un CORS ouvert à tout internet vu l'usage strictement local.

Exemple de `docker-compose.yml` :

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
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Data__TracksPath=/data/tracks.json
      - Data__StatsPath=/data/stats.json
      - Data__AudioPath=/data/audio
      - Data__CoversPath=/data/covers
      - Data__RootPath=/data
```

`Data__RootPath` est servi en fichiers statiques sous `/files` par le backend — c'est ce qui permet au host de lire l'audio par HTTP (`GET /files/audio/xxx.mp3`), les chemins de `tracks.json` étant déjà relatifs à cette racine. Seul le host y accède, jamais les joueurs.

Les volumes audio/covers sont montés en lecture seule (`ro`) côté conteneur — les scripts de préparation des données (sync Spotify, téléchargement YouTube, export/import CSV) écrivent directement sur le HDD, en dehors de Docker, donc le conteneur n'a besoin que de lire.

Le backend reste toujours actif sur le Raspberry Pi (déjà utilisé comme homelab). Aucune synchronisation de fichiers à faire avant une partie : le PC host et les téléphones se connectent simplement à l'IP du Pi sur le réseau local.

## 3. Pipeline de préparation des données

1. **Récupération playlist** : appel API Spotify pour obtenir les morceaux d'une playlist (titre, artiste, album, ID Spotify).
2. **Enrichissement automatique des genres** : pour chaque artiste unique, appel batché (jusqu'à 50 artistes/requête) à `GET /artists` pour récupérer le champ `genres`. Peu de requêtes nécessaires même pour ~1000 morceaux.
3. **Tags thématiques manuels/semi-automatiques** : les genres Spotify ne couvrent pas les thèmes personnalisés (Disney, années 90, etc.) — à compléter à la main ou via un passage assisté (script/LLM), avec relecture humaine ensuite.
4. **Téléchargement audio** : pour chaque morceau, recherche + téléchargement YouTube (ex. via yt-dlp), fichier stocké sur le HDD, chemin enregistré dans `tracks.json`.
   - **Validation du matching** : après téléchargement, comparer la durée réelle du fichier audio à `durationMs` (Spotify), tolérance ±5-10s. Tout écart au-delà flague le morceau comme "à vérifier manuellement" plutôt que de l'intégrer tel quel au catalogue — évite qu'une mauvaise version (live, reprise, lyric video d'un autre artiste) se retrouve jouée en pleine partie.
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
  "tags": ["disney", "annees-90", "dessin-anime"],
  "trapWith": ["idAutreMorceau1", "idAutreMorceau2"],
  "year": 1989,
  "filePath": "audio/a1b2c3.mp3",
  "coverPath": "covers/a1b2c3.jpg",
  "refrainStartMs": null,
  "addedAt": "2026-08-06T10:00:00Z"
}
```

- `genres` : rempli automatiquement depuis Spotify (par artiste).
- `tags` : thèmes personnalisés, remplis manuellement ou semi-automatiquement.
- `trapWith` : IDs de morceaux fréquemment confondus (ex. Axel F / Crazy Frog), utilisés pour générer des QCM pièges.
- `coverPath` : pochette d'album téléchargée localement depuis Spotify, utilisée sur les écrans de jeu pour l'esthétique.
- `title` : nettoyé à l'import (voir section 3) des suffixes d'édition ("- Radio Edit", "- ... Remix", "(feat. ...)") — trop de bruit dans le titre le rend impossible à taper en mode `TapeReponse`/`PremiereLettre`.
- `refrainStartMs` : optionnel, `null` par défaut (comportement actuel : lecture depuis le début du fichier). Renseigné manuellement, indique le point de départ (ms) à jouer pendant le round plutôt que le début du morceau — utile quand l'intro n'est pas identifiable.

**Stockage** : `tracks.json` = source de vérité unique pour les métadonnées éditoriales, versionnable avec Git, chargé en mémoire par le backend au démarrage (`List<Track>` + LINQ pour le filtrage). Pas de base de données pour l'instant — largement suffisant pour ce volume, à réévaluer seulement si le catalogue dépasse les dizaines de milliers de morceaux ou si des écritures concurrentes deviennent nécessaires.

### `stats.json` — compteurs runtime, séparés de `tracks.json`

`playCount` (et tout autre compteur alimenté pendant une partie) vit dans un fichier séparé, `data/stats.json`, indexé par `id` de morceau :

```json
{
  "a1b2c3": { "playCount": 3 }
}
```

**Raison de la séparation** : le backend tourne en continu (section 2) et doit persister `playCount` sur disque pour survivre à ses redémarrages — il a donc forcément besoin d'écrire quelque part. Si ce compteur vivait dans `tracks.json`, le backend aurait besoin d'un accès en écriture sur ce fichier, ce qui entrerait en collision avec le script d'import CSV (section 3bis) qui réécrit `tracks.json` en entier — potentiellement à tout moment, puisque le backend est toujours actif. En séparant les deux fichiers, cette collision disparaît complètement : le script de curation de tags ne touche jamais à `stats.json`, et le backend ne touche jamais à `tracks.json` en écriture (d'ailleurs monté `:ro` dans le conteneur, voir section 2). Conséquence pratique : grâce à cette séparation totale, le script d'import CSV peut être relancé à tout moment, y compris pendant une partie en cours, sans aucun risque pour les stats runtime — le backend charge `tracks.json` uniquement au démarrage, donc une réédition n'a d'effet qu'au redémarrage suivant.

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

**Pourquoi une pénalité asymétrique (×0.5) plutôt que symétrique** : ne pas répondre du tout coûte déjà -5 points fixes (étape 4), donc l'abstention n'est jamais "gratuite" — la question est seulement de savoir à partir de quel niveau de certitude tenter sa chance devient rentable. Avec une pénalité égale au gain (×1), deviner sur un QCM à 4 options sans aucun indice donne une espérance de `-0.5 × pointsEnJeu` : pire que les -5 fixes de l'abstention, donc un joueur hésitant a mathématiquement intérêt à ne jamais répondre — à l'encontre de l'esprit "tout le monde participe". Avec ×0.5, ce même guess à l'aveugle reste à espérance négative (`-0.125 × pointsEnJeu`, ce n'est pas un moyen de "rentabiliser le hasard pur"), mais dès que le joueur a éliminé ne serait-ce qu'une option parmi les 4 (3 candidats restants), l'espérance devient nulle, et à 2 candidats restants elle devient nettement positive (`+0.25 × pointsEnJeu`). Le rôle du ×0.5 est donc d'inciter à répondre dès qu'on a un minimum d'indice, pas de rendre le pur hasard profitable, tout en gardant un vrai coût à l'erreur.

### Cible de la question (titre ou auteur)

Chaque round tire aléatoirement (50/50, indépendamment du mode QCM/TapeReponse/PremiereLettre) une **cible** — `Titre` ou `Auteur` — annoncée au joueur ("trouve le titre" / "trouve l'artiste"). Ajouté suite à un retour de playtest : sans cible explicite, un morceau à plusieurs auteurs (ex. featurings) rendait le mode `TapeReponse` quasi injouable (fallait taper la liste complète) et le mode `PremiereLettre` ambigu (première lettre de quoi ?).

- **QCM** : les options affichent uniquement le champ correspondant à la cible (titre ou un seul auteur par option), jamais les deux concaténés.
- **TapeReponse / PremiereLettre**, cible `Auteur` : le champ `artist` peut lister plusieurs noms séparés par des virgules (ex. `"David Guetta, Tones And I, Teddy Swims"`) — **n'importe lequel** des auteurs listés est accepté comme réponse correcte, pas besoin de tous les citer.
- La cible n'affecte jamais la validation en mode QCM (toujours par sélection d'ID) ni l'écran de révélation (`RoundEnded` affiche toujours titre **et** artiste complets, quelle que soit la cible du round qui vient de se terminer).

### Génération des QCM

- Par défaut : 3 distracteurs tirés aléatoirement dans le même pool genre/tag que le morceau à deviner.
- Si le morceau a des `trapWith` définis : probabilité configurable (~15-20 %) d'utiliser un piège plutôt qu'un distracteur aléatoire, pour ne pas que ça tombe trop souvent sur plusieurs parties.
- **Fallback pool insuffisant** : si le pool genre/tag ne contient pas assez de morceaux distincts pour compléter les 3 distracteurs (thème trop niche, ou série mal configurée), compléter avec des morceaux tirés du pool global (tout `tracks.json`), même hors thème — garantit toujours 4 options valides plutôt qu'un crash ou un round bloqué. Le QCM est alors un peu plus facile dans ce cas limite, ce qui est préférable à l'absence de round.

## 7. Question bonus (fin de série)

Mécanique en deux phases, mise choisie **à l'aveugle** avant de découvrir la question. *La durée de la phase mise et celle de la phase question sont configurables (voir section 10).*

1. **Phase mise** — le serveur annonce les 4 paliers de la série courante (safe / moyen / moyen+ / risqué, définis dans une table de config par série, croissants jusqu'à 3000 pts en fin de partie). Chaque joueur choisit un palier via `SelectStake(index)`. Délai limite (~15s) : pas de choix → palier "safe" appliqué par défaut.
2. **Phase question** — une fois tous les choix reçus (ou le délai passé), le morceau est révélé et un timer fixe démarre, **sans dégressivité**. Le morceau est joué **ralenti** par défaut pour complexifier la tâche (`playbackRate` réduit côté lecteur audio du host, ex. 0.8) — paramètre `ralentissementBonusActivé` (bool) désactivable, avec un facteur configurable. Un seul essai par joueur. Pas de réponse dans le temps imparti → traité comme une réponse fausse (perte de la mise).
3. **Résultat** — réponse juste : `+mise` ; réponse fausse ou absence de réponse : `-mise`.
4. **Tableau général** — affiché **au moins une fois par partie** (pas systématiquement à chaque série). Par défaut, déclenché automatiquement après la série médiane (`⌈nombreDeSéries / 2⌉`), et le host peut aussi le déclencher manuellement à tout moment via une commande dédiée (`ShowLeaderboard()`).

Table de config des paliers par série (exemple, à ajuster) :

```
Série 1: [10, 20, 30, 50]
Série 2: [50, 100, 150, 250]
...
Série N (dernière): [500, 1000, 2000, 3000]
```

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
| `CreateGame(tags, séries)` | Crée la partie, sélectionne le pool de morceaux |
| `RejoinAsHost(code)` | Resynchronise le host après un refresh/crash de l'onglet : renvoie l'état courant complet (morceau en cours, mode, position audio théorique calculée depuis `débutRound`/`duréeEnPauseMs`, `enPause`) pour reprendre la lecture au bon endroit sans redémarrer le morceau |
| `StartRound()` | Démarre un round classique, horodate `débutRound` |
| `StartBonusRound()` | Démarre la phase de mise d'une question bonus |
| `ShowLeaderboard()` | Déclenche manuellement l'affichage du tableau général |
| `PauseGame()` / `ResumeGame()` | Gèle/reprend la partie en cours |
| `ValidateAnswerManually(playerId, correct)` | Override manuel pour les réponses texte ambiguës |
| `NextRound()` | Passe au round suivant |
| `EndGame()` | Termine la partie |
| `RejouerPartie()` | Uniquement si la partie est `Terminée` : relance une nouvelle manche avec le même code et les mêmes joueurs — mêmes configs/modes de série qu'à la création mais nouvelle sélection de morceaux, scores remis à zéro, session repassée en `Lobby`. Évite aux joueurs de retaper le code entre deux manches. |

### Méthodes déclenchées par les joueurs

| Méthode | Effet |
|---|---|
| `JoinGame(code, nom, playerId)` | Rejoint la partie. `playerId` est un identifiant stable généré et persisté côté client Flutter (pas le `connectionId` SignalR, qui change à chaque reconnexion). Si ce `playerId` existe déjà dans la partie (reconnexion après coupure réseau), le serveur réassocie simplement le nouveau `connectionId` au `Player` existant et renvoie son état (score, équipe) au lieu de créer un nouveau joueur |
| `JoinTeam(teamId)` | Rejoint une équipe (si `modeÉquipe` actif) |
| `SubmitAnswer(payload)` | Soumet une réponse (round classique) |
| `SelectStake(index)` | Choisit un palier de mise (phase 1 bonus) |
| `SubmitBonusAnswer(payload)` | Soumet une réponse (phase 2 bonus) |

### Événements diffusés par le serveur

| Événement | Contenu |
|---|---|
| `PlayerJoined` | Infos du joueur (nouveau joueur) |
| `PlayerReconnected` / `PlayerDisconnected` | Changement d'état `estConnecté` d'un joueur existant (perte réseau, reconnexion) |
| `RoundStarted` | Morceau (mode-dépendant), mode, cible (Titre/Auteur — voir section 6), URL audio + `refrainStartMs` (host uniquement) |
| `ScoreUpdate` | Scores à jour de tous les joueurs |
| `RoundEnded` | Réponse correcte, détail des points de chacun |
| `BonusStakeOptions` | Les 4 paliers de la série courante |
| `BonusQuestionStarted` | Morceau révélé, timer fixe démarré |
| `BonusResult` | Résultat de chaque joueur (mise gagnée/perdue) |
| `LeaderboardShown` | Classement général, diffusé en fin de série |
| `GamePaused` / `GameResumed` | État de pause |
| `GameEnded` | Scores finaux |
| `GameRestarted` | Diffusé après `RejouerPartie()` — ramène tous les clients à l'écran du lobby (même code, mêmes joueurs, scores à zéro) |

## 11. Paramètres de configuration

Tous ces éléments sont des paramètres de partie/série, pas des valeurs figées dans le code :

| Paramètre | Niveau | Notes |
|---|---|---|
| Nombre de rounds classiques par série | Série | Peut varier d'une série à l'autre au sein d'une même partie |
| Durée de la fenêtre de réponse (round classique) | Série ou global | Utilisée dans la formule de scoring dégressif |
| Durée de la phase mise (question bonus) | Série | Délai avant application du palier "safe" par défaut |
| Durée de la phase question (question bonus) | Série | Pas de dégressivité, juste une limite dure |
| Paliers de mise (4 valeurs) | Série | Table de config croissante, voir section 7 |
| Probabilité d'un QCM piège | Global | ~15-20 % par défaut, ajustable |
| Seuil de tolérance Levenshtein | Global | Voir recommandation ci-dessous |
| Ralentissement audio (question bonus) | Global | Activé/désactivé + facteur de ralentissement (ex. 0.8), voir section 7 |
| Affichage du tableau général | Partie | Au moins une fois par partie, par défaut après la série médiane, déclenchable aussi manuellement par le host |
| Mode équipe | Partie | Activé/désactivé, voir section 8 |

Concrètement, ça se traduit par une classe `SeriesConfig` (nombre de rounds, durée réponse, paliers de mise, durées des phases bonus) instanciée par série au moment de `CreateGame`, plutôt que des constantes fixes dans le code.

### Seuil de tolérance Levenshtein — recommandation

Aucune valeur universelle ne convient à tous les titres (un titre de 3 caractères et un de 30 caractères n'ont pas la même tolérance à l'erreur). Je recommande un seuil proportionnel à la longueur du texte normalisé (minuscules, accents retirés, ponctuation ignorée) :

```
seuil = max(1, floor(longueur(texteNormalisé) × 0.2))
```

Concrètement : ~20 % de caractères d'écart tolérés, avec un minimum de 1. Ça reste un point de départ — à ajuster après quelques parties de test si ça se montre trop laxiste (des réponses clairement fausses validées) ou trop strict (des réponses correctes rejetées pour une faute de frappe).

### Outil de curation des tags — recommandation

Plutôt que de construire une interface web dédiée (temps de dev pour un usage ponctuel), je recommande un aller-retour par tableur :

1. Script d'export : `tracks.json` → CSV avec colonnes `id`, `title`, `artist`, `genres` (déjà rempli par Spotify), `tags` (vide ou pré-rempli par heuristique).
2. Édition dans Excel (que tu maîtrises déjà) — tri, filtre, remplissage par glisser-copier pour les morceaux d'un même thème, éventuellement une liste de validation de données pour les tags courants.
3. Script d'import : CSV → réinjection dans `tracks.json`.

Plus rapide à mettre en place qu'une UI web, et plus confortable pour l'édition en masse de ~1000 lignes.

### Égalités en fin de partie

Décision : les égalités sont acceptées telles quelles, pas de mécanisme de départage. Le classement final peut afficher plusieurs joueurs ex-æquo.
