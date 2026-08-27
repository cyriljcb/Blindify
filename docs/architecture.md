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

### Nom local au lieu de l'IP (mDNS/Bonjour)

Retour utilisateur (2026-08-25) : taper l'IP du Pi depuis un iPhone est pénible. Pas besoin d'un vrai serveur DNS local (overkill pour un usage familial) — le mDNS (Bonjour) suffit et est nativement supporté par iOS/Safari et macOS, sans rien installer côté client.

- Sur le Pi : `sudo raspi-config` → *System Options* → *Hostname*, renommer en `blindify` (avahi-daemon, qui fait tourner le mDNS, est généralement déjà présent et actif par défaut sur Raspberry Pi OS — vérifier avec `systemctl status avahi-daemon`, sinon `sudo apt install avahi-daemon`).
- L'app devient joignable en `http://blindify.local:5000` (panneau de contrôle) et `http://blindify.local:5000/display.html` (écran public), à la place de l'IP.
- Config système sur le Pi, **aucun changement côté backend/Docker** : avahi tourne sur l'hôte, pas dans le conteneur, et n'a besoin de rien savoir du port publié.
- Limite : dépend du support mDNS du réseau Wi-Fi — impeccable sur une box/routeur familial classique, plus capricieux si le réseau isole les clients entre eux (peu probable en usage domestique).

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

**Usage dans la sélection** (ajouté le 2026-08-25, retour utilisateur : le compteur s'incrémentait bien mais n'était lu nulle part, donc un même thème ressortait souvent avec les mêmes morceaux d'une partie à l'autre) : `RoundService.SelectionnerMorceaux` accepte un paramètre optionnel `Func<string, int> playCount` — quand fourni (c'est le cas dans `GameHub`, via `statsRepository.GetPlayCount`), le tirage n'est plus uniforme mais pondéré, poids `1/(playCount+1)`. Un morceau jamais joué a donc plus de chances de sortir qu'un morceau déjà joué plusieurs fois, sans jamais l'exclure totalement (pondération "douce", pas un anti-répétition strict). `RoundService` reste indépendant de `Blindify.Infrastructure` : il reçoit un délégué plutôt que `IStatsRepository` directement, cohérent avec le fait qu'il reçoit déjà le pool de morceaux en `IReadOnlyList<Track>` plutôt que `ITracksRepository`.

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

### Cible de la question (titre, auteur ou film)

Chaque round tire aléatoirement (50/50, indépendamment du mode QCM/TapeReponse/PremiereLettre) une **cible** — `Titre` ou `Auteur` — annoncée au joueur ("trouve le titre" / "trouve l'artiste"). Ajouté suite à un retour de playtest : sans cible explicite, un morceau à plusieurs auteurs (ex. featurings) rendait le mode `TapeReponse` quasi injouable (fallait taper la liste complète) et le mode `PremiereLettre` ambigu (première lettre de quoi ?).

**Exception — morceaux tagués `"disney"`** : la cible est **toujours forcée à `Film`**, jamais tirée au hasard. Ni le titre réel de la chanson ni l'artiste crédité (souvent la voix/l'acteur, ex. "Jason Weaver, Rowan Atkinson, Laura Williams") ne sont des questions jouables pour ce type de contenu — la question naturelle est le film dont est tiré le morceau. Le nom du film est déduit de `Track.Album` (nettoyé des suffixes de bande originale courants côté Spotify — "(Original Motion Picture Soundtrack)", "X Original Soundtrack (French Version)", etc. — voir `FilmNameResolver`), avec priorité à une mention explicite dans le titre lui-même quand elle existe (ex. `"Il vit en toi - Extrait de \"Le roi lion 2\""` → "Le roi lion 2") — plus fiable qu'un album de compilation qui ne nomme aucun film. Cette même règle s'applique à la question bonus (`BonusRoundService.CreerBonusRound`) : un morceau "disney" tiré en bonus demande aussi le film.

- **QCM** : les options affichent uniquement le champ correspondant à la cible (titre, un seul auteur, ou film par option), jamais plusieurs champs concaténés.
- **TapeReponse / PremiereLettre**, cible `Auteur` : le champ `artist` peut lister plusieurs noms séparés par des virgules (ex. `"David Guetta, Tones And I, Teddy Swims"`) — **n'importe lequel** des auteurs listés est accepté comme réponse correcte, pas besoin de tous les citer.
- La cible n'affecte jamais la validation en mode QCM (toujours par sélection d'ID). En revanche elle affecte bien l'écran de révélation : `RoundEnded`/`BonusResult` transportent la cible du round et le film déduit, et l'écran met en avant le film comme réponse quand `cible == Film` (le vrai titre/artiste restent affichés en dessous, à titre de trivia) — pour les cibles Titre/Auteur, le titre et l'artiste complets restent affichés normalement.

### Génération des QCM

- Par défaut : 3 distracteurs tirés aléatoirement dans le même pool genre/tag que le morceau à deviner.
- **Fallback pool insuffisant** : si le pool genre/tag ne contient pas assez de morceaux distincts pour compléter les 3 distracteurs (thème trop niche, ou série mal configurée), compléter avec des morceaux tirés du pool global (tout `tracks.json`), même hors thème — garantit toujours 4 options valides plutôt qu'un crash ou un round bloqué. Le QCM est alors un peu plus facile dans ce cas limite, ce qui est préférable à l'absence de round.

Trois niveaux de piège indépendants, chacun avec sa propre probabilité (`GameConfig`), pour ne pas que ça tombe trop souvent sur plusieurs parties :

| Niveau | Source du leurre | Cliquer dessus | Config |
|---|---|---|---|
| Piège réel | Un autre vrai morceau du catalogue (`trapWith`) | Compte comme ce morceau réel | `ProbabiliteQcmPiege` (5 % par défaut) |
| Feinte champ croisé | Le champ opposé du morceau correct lui-même (ex. cible Auteur -> affiche son propre titre) | Mauvaise réponse normale contre le distracteur tiré au sort | `ProbabiliteQcmFeinteChamp` (10 % par défaut) |
| Feinte texte inventé | Un texte écrit à la main (`trapTextArtist`), sans rapport avec un morceau réel (ex. Bastille - Pompéi -> "Baptiste") | Mauvaise réponse normale contre le distracteur tiré au sort | `ProbabiliteQcmFeinteTexteArtiste` (5 % par défaut, cible Auteur uniquement) |

Dans les deux cas de feinte, le `TrackId` du distracteur affiché ne change jamais — seul le texte affiché (titre ou artiste) est substitué, le scoring reste celui du distracteur réellement tiré.

## 7. Question bonus (fin de série)

Mécanique en deux phases, mise choisie **à l'aveugle** avant de découvrir la question. *La durée de la phase mise et celle de la phase question sont configurables (voir section 10).*

1. **Phase mise** — le serveur annonce les 4 paliers de la série courante (safe / moyen / moyen+ / risqué, définis dans une table de config par série, croissants jusqu'à 3000 pts en fin de partie). Chaque joueur choisit un palier via `SelectStake(index)`. Délai limite (~15s) : pas de choix → palier "safe" appliqué par défaut.
2. **Phase question** — une fois tous les choix reçus (ou le délai passé), le morceau est révélé et un timer fixe démarre, **sans dégressivité**. Le morceau est joué **ralenti** par défaut pour complexifier la tâche (`playbackRate` réduit côté lecteur audio du host, ex. 0.8) — paramètre `ralentissementBonusActivé` (bool) désactivable, avec un facteur configurable. Un seul essai par joueur. Pas de réponse dans le temps imparti → traité comme une réponse fausse (perte de la mise).
3. **Résultat** — réponse juste : `+mise` ; réponse fausse ou absence de réponse : `-mise`.
4. **Tableau général** — affiché **au moins une fois par partie** (pas systématiquement à chaque série). Par défaut, déclenché automatiquement après la série médiane (`⌈nombreDeSéries / 2⌉`), et le host peut aussi le déclencher manuellement à tout moment via une commande dédiée (`ShowLeaderboard()`).

**Enchaînement côté host (`host/`)** — comportement de la page web, pas une règle du contrat serveur : une fois la série classique **courante** épuisée, le host déclenche automatiquement `StartBonusRound()` (au lieu d'attendre une intervention) pour la question bonus de **cette** série. Après réception de `BonusResult`, deux cas : s'il reste une série suivante dans la partie, le host affiche un compte à rebours puis enchaîne automatiquement sur son premier round (`NextRound()` + `StartRound()`, bouton "Série suivante maintenant" disponible pour ne pas attendre) ; sinon (dernière série), le host affiche un compte à rebours et déclenche automatiquement `EndGame()` (bouton "Terminer maintenant"). Le host garde la main pour interrompre cet enchaînement (pause, tableau général) à tout moment.

Table de config des paliers par série — chaque `SeriesConfig.PaliersDeMise` est fourni tel quel par le client dans `ConfigurerPartie` (jamais calculé côté serveur, voir section 11). Le panneau de contrôle (`host/app.js`, `paliersPourSerie`) génère par défaut une progression géométrique à partir de la série de base `[10, 20, 30, 50]`, avec une raison calculée pour que le palier le plus haut atteigne exactement 3000 pts à la **dernière** série de la partie — pas un facteur fixe : la raison dépend du nombre de séries réellement choisi pour cette partie (`raison = (3000/50)^(1/(nombreSéries-1))`). Exemple avec 10 séries (raison ≈ 1,576) :

```
Série 1  (index 0) : [10, 20, 30, 50]
Série 2  (index 1) : [16, 32, 47, 79]
Série 5  (index 4) : [62, 123, 185, 309]
Série 10 (index 9) : [600, 1200, 1800, 3000]
```

Avec une seule série, pas de progression possible : les paliers restent `[10, 20, 30, 50]`.

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

### Méthodes déclenchées par les joueurs

| Méthode | Effet |
|---|---|
| `JoinGame(code, nom, playerId)` | Rejoint la partie. `playerId` est un identifiant stable généré et persisté côté client Flutter (pas le `connectionId` SignalR, qui change à chaque reconnexion). Si ce `playerId` existe déjà dans la partie (reconnexion après coupure réseau), le serveur réassocie simplement le nouveau `connectionId` au `Player` existant et renvoie son état (score, équipe) au lieu de créer un nouveau joueur |
| `JoinTeam(teamId)` | Rejoint (ou change d')équipe — autorisé à tout moment, pas seulement au lobby. La liste des équipes disponibles est renvoyée par `JoinGame`, pas besoin d'appel séparé pour les découvrir |
| `SubmitAnswer(payload)` | Soumet une réponse (round classique) |
| `SelectStake(index)` | Choisit un palier de mise (phase 1 bonus) |
| `SubmitBonusAnswer(payload)` | Soumet une réponse (phase 2 bonus) |

### Événements diffusés par le serveur

| Événement | Contenu |
|---|---|
| `PlayerJoined` | Infos du joueur (nouveau joueur) |
| `PlayerReconnected` / `PlayerDisconnected` | Changement d'état `estConnecté` d'un joueur existant (perte réseau, reconnexion) |
| `PlayerTeamChanged` | Un joueur a rejoint (ou changé d')équipe |
| `SerieAnnoncee` | Index de la série qui commence + ses tags (thème) — voir `AnnoncerSerieCourante()`. Retour utilisateur du 2026-08-24 : auparavant affiché uniquement côté host/écran public, jamais diffusé aux joueurs |
| `RoundStarted` | Morceau (mode-dépendant), mode, cible (Titre/Auteur/Film — voir section 6), URL audio + `refrainStartMs` (host uniquement — mémorisé côté host, appliqué au moment du `RoundEnded`, pas pendant la découverte). Options QCM (si applicable) incluent un champ `Film` par option, affiché à la place de titre/auteur quand la cible est Film |
| `ScoreUpdate` | Scores à jour de tous les joueurs |
| `RoundEnded` | Réponse correcte (titre + artiste), détail des points de chacun, cible du round et film déduit (`Cible`/`Film`) — l'écran de révélation met le film en avant quand `Cible == Film` |
| `BonusStakeOptions` | Les 4 paliers de la série courante |
| `BonusQuestionStarted` | Morceau révélé, timer fixe démarré + `refrainStartMs` (host uniquement — appliqué au `BonusResult`, pas pendant la devinette). Version joueurs inclut la cible (Titre/Film) |
| `BonusResult` | Résultat de chaque joueur (mise gagnée/perdue), cible et film déduit (mêmes champs `Cible`/`Film` que `RoundEnded`) |
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
| Probabilité d'un QCM piège réel (`trapWith`) | Global | 5 % par défaut, ajustable |
| Probabilité d'une feinte champ croisé | Global | 10 % par défaut, ajustable |
| Probabilité d'une feinte texte inventé (`trapTextArtist`) | Global | 5 % par défaut, ajustable, cible Auteur uniquement |
| Seuil de tolérance Levenshtein | Global | Voir recommandation ci-dessous |
| Ralentissement audio (question bonus) | Global | Activé/désactivé + facteur de ralentissement (ex. 0.8), voir section 7 |
| Affichage du tableau général | Partie | Au moins une fois par partie, par défaut après la série médiane, déclenchable aussi manuellement par le host |
| Mode équipe | Partie | Activé/désactivé, voir section 8 |

Concrètement, ça se traduit par une classe `SeriesConfig` (nombre de rounds, durée réponse, paliers de mise, durées des phases bonus) instanciée par série au moment de `ConfigurerPartie`, plutôt que des constantes fixes dans le code.

### Seuil de tolérance Levenshtein — recommandation

Aucune valeur universelle ne convient à tous les titres (un titre de 3 caractères et un de 30 caractères n'ont pas la même tolérance à l'erreur). Je recommande un seuil proportionnel à la longueur du texte normalisé (minuscules, accents retirés, ponctuation ignorée) :

```
seuil = max(1, floor(longueur(texteNormalisé) × 0.2))
```

Concrètement : ~20 % de caractères d'écart tolérés, avec un minimum de 1. Ça reste un point de départ — à ajuster après quelques parties de test si ça se montre trop laxiste (des réponses clairement fausses validées) ou trop strict (des réponses correctes rejetées pour une faute de frappe).

### Outil de curation des tags

Plutôt qu'une interface web dédiée (temps de dev pour un usage ponctuel), un aller-retour par tableur — implémenté dans `data/scripts/` suite au retour utilisateur du 2026-08-24 (morceaux "variété française" mal tagués "electro" par le bucketing automatique, voir section 3 point 3) :

1. `python data/scripts/export_tags_csv.py [--tag TAG]` : `tracks.json` → CSV avec colonnes `id`, `title`, `artist`, `year`, `genres` (lecture seule), `tags` (éditable) — `--tag` limite l'export à une seule catégorie à relire (ex. `--tag electro`).
2. Édition de la colonne `tags` au tableur (Excel — encodage `utf-8-sig`) — tri, filtre, remplissage par glisser-copier pour les morceaux d'un même thème.
3. `python data/scripts/import_tags_csv.py CHEMIN.csv [--dry-run]` : réinjecte uniquement la colonne `tags` par `id` (les autres colonnes du CSV sont ignorées) — un CSV filtré par `--tag` peut être réimporté tel quel sans toucher au reste du catalogue.

Plus rapide à mettre en place qu'une UI web, et plus confortable pour l'édition en masse de ~1000 lignes.

### Égalités en fin de partie

Décision : les égalités sont acceptées telles quelles, pas de mécanisme de départage. Le classement final peut afficher plusieurs joueurs ex-æquo.
