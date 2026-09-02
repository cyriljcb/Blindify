# Blindify — Audit complet du code existant (en vue d'une refonte d'architecture)

> Document généré pour servir de contexte à une discussion de refonte architecturale, dans une conversation Claude séparée (copier-coller intégral en premier message). Il combine : le contexte projet, la liste des fonctionnalités du jeu, et un inventaire **fichier par fichier** de tout le code (backend .NET, app Flutter, client host web, pipeline de données Python). L'objectif n'est pas de proposer une nouvelle architecture ici, mais de donner une base d'audit complète et fiable pour en discuter ensuite.
>
> État du repo au moment de l'audit (2026-08-29) : plusieurs fichiers backend/app/host modifiés mais non commités (features en cours : mode "course" pour la question bonus, redémarrage serveur à distance), quelques scripts data non trackés (nouveaux outils d'audit de genres, éditeur de tags visuel). Le document décrit l'état du code **sur disque**, pas seulement ce qui est commité.

---

## 1. Contexte projet

Blindtest local multijoueur (buzzer/quiz en réseau local), remplace un ancien système type QuizzXpress (lui-même une réécriture complète d'un projet Blindify précédent en Spring Boot + Angular).

**Stack** :

| Composant | Techno |
|---|---|
| Backend | ASP.NET Core + SignalR |
| App joueur | Flutter (buzzer, réponses — jamais d'audio) |
| Client host | Page web vanilla JS (pas de build tooling), PC relié à une enceinte |
| Stockage | `data/tracks.json` (métadonnées, source de vérité — **pas de DB**) + `data/stats.json` (compteurs runtime, séparé pour éviter tout conflit d'écriture) |
| Audio/covers | Fichiers locaux sur HDD 2 To (Raspberry Pi), téléchargés depuis YouTube + métadonnées Spotify |
| Déploiement | Backend dockerisé sur le Raspberry Pi, réseau local uniquement (WiFi maison), pas d'accès distant |

**Contraintes structurantes actuelles** (à respecter ou à remettre en question explicitement dans toute proposition de refonte) :
- L'audio ne transite **jamais** vers les téléphones — seul le client host (PC) lit l'audio.
- Réponses **indépendantes par joueur**, pas de verrouillage/buzzer exclusif (un seul essai par round).
- Tous les timings/scores/paliers sont configurables par partie ou par série (`SeriesConfig`/`GameConfig`), jamais en dur.
- `tracks.json` = source de vérité unique tant que le catalogue reste petit (~1000-2000 morceaux aujourd'hui) ; le backend ne l'écrit jamais (monté en lecture seule dans le conteneur).
- Joueurs identifiés par un `playerId` stable généré côté Flutter, jamais par le `connectionId` SignalR (survit aux coupures réseau).
- Seul le backend est dockerisé ; le frontend (Flutter + page host) n'est jamais construit/transformé par Docker.

Le détail complet des règles du jeu (formules exactes, contrat SignalR méthode par méthode, schéma `tracks.json`) est dans `docs/architecture.md` du repo.

---

## 2. Fonctionnalités du jeu (vue d'ensemble)

### Cycle de vie d'une partie
1. **Lobby** — le host crée la partie (`CreateGame`, minimal : code + secret host + équipes), configure séparément le blindtest (`ConfigurerPartie` : thèmes/tags par série, nombre de rounds, durées, paliers de mise), rappelable tant que la partie n'a pas démarré. Les joueurs rejoignent via code ou QR code pendant ce temps.
2. **Annonce de série** — écran dédié avant le premier round de chaque série (thème affiché aux 3 clients : host, écran public, joueurs).
3. **Rounds classiques** — N rounds par série, chacun : lecture audio déclenchée par le host, réponses indépendantes (QCM / réponse tapée / première lettre), scoring dégressif selon la vitesse, résultat diffusé à tous.
4. **Question bonus** — en fin de série : phase de mise à l'aveugle (4 paliers croissants) puis phase question (morceau ralenti, sans dégressivité), avec un mode "course" optionnel (QCM uniquement : le premier qui répond décide seul du sort de sa mise).
5. **Tableau général** — affiché au moins une fois par partie (auto après la série médiane, ou manuel).
6. **Fin de partie** — classement final, égalités acceptées sans départage, possibilité de "rejouer" (même code/joueurs, nouvelle sélection de morceaux, scores à zéro).

### Mécaniques de jeu notables
- **Cible de la question** (Titre / Auteur / Film) tirée à 50/50 par round (sauf morceaux "disney" → toujours Film, déduit de l'album/titre via `FilmNameResolver`).
- **Scoring dégressif** : `pointsEnJeu(t) = max(min, max - (tempsÉcoulé/durée) × (max-min))` ; mauvaise réponse = `-pointsEnJeu × 0.5` (pénalité asymétrique volontaire, voir `docs/architecture.md` section 6 pour la justification mathématique) ; absence de réponse = `-5` pts fixes.
- **Génération QCM** : 3 distracteurs tirés du pool thématique via un système d'"ancre unique" (genre/tag partagé par tous les distracteurs, pas juste avec la bonne réponse) + barrière francophone, avec repli en cascade (année proche → pool global) ; 3 niveaux de pièges configurables (vrai morceau confondu, feinte champ croisé, feinte texte inventé).
- **Tolérance de réponse texte** : distance de Levenshtein avec seuil à 3 zones selon la longueur du texte attendu (exact en dessous de 4 caractères, tolérance fixe entre 4 et 10, ratio proportionnel au-delà).
- **Mode équipes** optionnel : scores agrégés par équipe, réponses toujours individuelles.
- **Pause de partie** : gèle timers/audio, neutralise le temps de pause dans le calcul des points.
- **Reconnexion** : `playerId` stable survit aux coupures réseau (téléphone) ; `RejoinAsHost` avec secret dédié pour le panneau de contrôle (refresh/crash d'onglet).
- **Redémarrage serveur à distance** (feature récente) : `POST /api/admin/restart`, protégé par mot de passe, désactivé par défaut.
- **Sélection pondérée des morceaux** : les morceaux moins joués (compteur `stats.json`) ont plus de chances d'être tirés, sans exclusion stricte.

---

## 3. Inventaire fichier par fichier — Backend (ASP.NET Core + SignalR)

Architecture en couches classique et bien respectée : `Domain` (entités/enums/config purs, zéro dépendance) → `Application` (logique métier : scoring, QCM, matching de réponses, cycle de vie round/bonus, sessions) → `Infrastructure` (accès disque tracks.json/stats.json) → `Api` (Program.cs, GameHub SignalR, Contracts DTOs, TimerCoordinators). `RoundService`/`BonusRoundService` restent indépendants de `Blindify.Infrastructure` (reçoivent des `IReadOnlyList<Track>`/`Func<string,int>` plutôt que les repositories directement) — bon respect de l'inversion de dépendance.

### Blindify.Domain

Aucune dépendance externe (ni ASP.NET, ni Application/Infrastructure) — entités et enums purs.

**Entities/GameSession.cs**
- Rôle : agrégat racine d'une partie en cours.
- Contenu clé : `Id`, `Etat` (GameState), `EnPause`/`PauseDemarreeA`, `ModeEquipe`, `Config` (GameConfig), `SeriesList`, `Players`, `Teams`, `HostConnectionId` (mutable), `HostSecret` (opaque, distinct du code public), `SerieCouranteIndex`, `RoundCourantIndex` (-1 = aucun round démarré).
- Dépendances : Configuration, Enums.
- Remarque : mutable en place partout dans le hub/services — pas d'immutabilité, pas d'event sourcing ; tout est CRUD direct sur les propriétés.

**Entities/Player.cs**
- Rôle : joueur d'une partie, identifié par `PlayerId` stable (généré côté Flutter), jamais par `ConnectionId` (mutable, réassocié à chaque reconnexion).
- Champs : `PlayerId`, `ConnectionId?`, `Nom`, `Score`, `TeamId?`, `EstConnecte`.

**Entities/Round.cs**
- Rôle : round classique.
- Champs : `TrackId`, `Mode` (RoundMode), `Cible` (RoundCible, tirée au démarrage), `DebutRound?`, `DureeEnPauseMs`, `Reponses` (List<RoundAnswer>), `QcmOptionTrackIds?` (mode Qcm uniquement).

**Entities/RoundAnswer.cs**
- Rôle : réponse d'un joueur à un round classique.
- Champs : `PlayerId`, `Timestamp`, `Reponse`, `EstCorrecte`, `Points`, `PointsEnJeu` (magnitude figée au moment de la réponse, permet de recalculer `Points` si `ValiderManuellement` change `EstCorrecte` après coup).

**Entities/Series.cs**
- Rôle : une série de rounds classiques + une question bonus.
- Champs : `Index`, `Config` (SeriesConfig), `Tags` (thème de cette série uniquement, vide = tout le catalogue), `Rounds`, `BonusRound?`.

**Entities/Team.cs**
- Rôle : équipe (mode équipe).
- Champs : `Id`, `Nom`. Pas de score stocké ici — calculé à la volée (voir `GameSessionNavigation.ScoresParEquipe`).

**Entities/Track.cs**
- Rôle : miroir en mémoire d'une entrée `tracks.json`.
- Champs : `Id`, `Title`, `Artist`, `Album?`, `SpotifyId?`, `YoutubeId?`, `DurationMs`, `Genres`, `Tags`, `TrapWith` (IDs de morceaux confondus), `TrapTextArtist?` (leurre texte inventé), `Year?`, `FilePath`, `CoverPath?`, `RefrainStartMs?`, `AddedAt`.

**Entities/BonusRound.cs**
- Rôle : question bonus de fin de série.
- Champs : `TrackId`, `Cible` (par défaut Titre, forcée Film si disney), `Mode` (tiré au hasard, par défaut TapeReponse), `QcmOptionTrackIds?`, `EstCourse` (mode Qcm uniquement), `DebutPhaseMise?`, `DebutPhaseQuestion?`, `DureeEnPauseMs`, `Mises` (List<BonusStake>), `Reponses` (List<BonusAnswer>).

**Entities/BonusAnswer.cs** / **BonusStake.cs**
- Réponse et mise d'un joueur à la question bonus. `BonusAnswer` : `PlayerId`, `Timestamp`, `Reponse`, `EstCorrecte`, `Points` (+mise/-mise). `BonusStake` : `PlayerId`, `PalierIndex`.

**Configuration/GameConfig.cs**
- Rôle : paramètres globaux à la partie (jamais de constantes en dur).
- Champs avec défauts : `ProbabiliteQcmPiege` (0.05), `ProbabiliteQcmFeinteChamp` (0.10), `ProbabiliteQcmFeinteTexteArtiste` (0.05), `SeuilToleranceLevenshteinRatio` (0.2), `LongueurMinimalePourTolerance` (10), `LongueurMinimalePourToleranceFixe` (4), `ToleranceFixeReponseCourte` (2), `ProbabiliteBonusCourse` (0.5), `RalentissementBonusActive` (true), `FacteurRalentissementBonus` (0.65).

**Configuration/SeriesConfig.cs**
- Rôle : paramètres instanciés par série.
- Champs : `NombreRoundsClassiques`, `DureeFenetreReponseMs`, `PointsMax`, `PointsMin`, `PenaliteMauvaiseReponseRatio` (0.5 défaut), `PenaliteAbsenceReponse` (-5 défaut), `PaliersDeMise` (int[4]), `DureePhaseMiseMs`, `DureePhaseQuestionMs`.

**Enums/GameState.cs** — `Lobby`, `EnCours`, `Termine`.
**Enums/RoundCible.cs** — `Titre`, `Auteur`, `Film` (forcée pour disney).
**Enums/RoundMode.cs** — `Qcm`, `TapeReponse`, `PremiereLettre`.

### Blindify.Application

Logique métier pure, référence Domain uniquement.

**Answers/AnswerMatcher.cs** (+ IAnswerMatcher)
- Rôle : validation floue des réponses texte.
- Méthodes : `DistanceLevenshtein(a,b)` (matrice DP classique), `Normaliser(texte)` (minuscules, accents retirés via `NormalizationForm.FormD`, ponctuation → espace unique, trim), `EstCorrecte(...)` (3 zones selon longueur normalisée : exact si < seuil fixe, tolérance fixe entre les deux seuils, ratio proportionnel au-delà).
- Aucune dépendance externe, entièrement testable en isolation.

**Bonus/BonusRoundService.cs** (+ IBonusRoundService)
- Rôle : cycle de vie complet de la question bonus.
- Méthodes : `CreerBonusRound` (tire Cible — Film forcé si disney, sinon 50/50 Titre/Auteur filtré par `TitreVariantes.EstEligibleCommeCible` — tire Mode uniformément parmi les 3, calcule `EstCourse` si Qcm+probabilité, génère les options Qcm via `RoundService.PoolPourQcm` + `IQcmGenerator`), `DemarrerPhaseMise`, `EnregistrerMise` (rejette si pause/phase question démarrée/déjà misé), `AppliquerPaliersParDefaut` (palier "safe" aux non-mise), `DemarrerPhaseQuestion`, `SoumettreReponse` (compare selon Mode/Cible, gère le cas Course — 2e réponse ignorée), `TerminerParTimeout` (pénalise les non-répondants, sauf en course déjà tranchée).
- Dépendances : IBonusScoringService, IAnswerMatcher, IQcmGenerator, `RoundService.PoolPourQcm` (statique interne), `TitreVariantes`, `AuteurVariantes`, `FilmNameResolver`.
- Remarque : `EstPremiereLettreCorrecte` dupliquée à l'identique dans `RoundService` — dette assumée et documentée en commentaire ("les deux services sont injectés/testés séparément").

**DependencyInjection/ServiceCollectionExtensions.cs**
- Rôle : enregistre tous les services Application en `Singleton` (`IScoringService`, `IBonusScoringService`, `IAnswerMatcher`, `IQcmGenerator`, `IRoundService`, `IBonusRoundService`, `IGameCodeGenerator`, `IGameSessionStore`). Tous singletons stateless sauf `GameSessionStore` (état partagé thread-safe via `ConcurrentDictionary`).

**Qcm/IQcmGenerator.cs** / **QcmGenerator.cs**
- Rôle : génère les 4 options d'un QCM (1 correcte + 3 distracteurs).
- Pipeline en paliers : (1) piège réel optionnel (`TrapWith`, filtré pour ne pas dupliquer l'auteur correct) ; (2) ancre unique — choisit le genre/tag *significatif* (fréquence ≤ 8% du pool, `SeuilPartFrequenceGenreGenerique`) du morceau correct ayant le plus de candidats éligibles, tous les distracteurs de ce palier doivent partager **cette même** ancre + le même statut `variete-francaise` (`MemeStatutFrancophone`) ; (3) repli année la plus proche si le morceau correct a une `Year` connue ; (4) repli pool global sans filtre ; (5) filet de sécurité final qui réutilise même des auteurs déjà choisis plutôt que de bloquer le round. Mélange Fisher-Yates final.
- Remarque : classe assez dense (~250 lignes) concentrant beaucoup de règles métier fines (barrière francophone, anti-doublon d'auteur entre distracteurs, seuil relatif de généricité) — bon candidat à découpage si la logique doit encore grossir.

**Qcm/QcmOptions.cs** — `record QcmOptions(string CorrectTrackId, IReadOnlyList<string> OptionsTrackIds)`.

**Rounds/AuteurVariantes.cs** — Rôle : `Acceptables(artist)` — split sur virgule, trim, retire les entrées vides. Partagé entre RoundService et BonusRoundService.

**Rounds/FilmNameResolver.cs**
- Rôle : déduit le nom du film pour la cible `Film` (morceaux disney) à partir de `Track.Title`/`Track.Album`.
- Logique regex : priorité au titre s'il contient `"Extrait de "X""`/`"De "X""` ; sinon nettoyage de l'album — format `"(From "X")"`, puis retrait itératif de la parenthèse finale + suffixes qualificatifs connus (`"original soundtrack"`, `"deluxe edition"`, etc.) jusqu'à stabilité. Best-effort documenté : les albums de compilation sans indice de film restent non résolus (retombent sur l'album tel quel).

**Rounds/GameSessionNavigation.cs**
- Rôle : extension methods de navigation sur `GameSession`. `SerieCourante()`, `RoundCourant()` (défensif sur `SeriesList` vide — lobby sans config), `ScoresParEquipe()` (somme des scores des membres par équipe, calculée à la volée, pas stockée).

**Rounds/IRoundService.cs** / **RoundService.cs**
- Rôle : cycle de vie complet d'un round classique.
- Méthodes : `SelectionnerMorceaux` (filtre par tags/genres via `FiltrerParTagsOuGenres`, tirage pondéré optionnel par `playCount` via `TirerPondere` — poids `1/(playCount+1)`, jamais nul ; **aucun repli catalogue complet si le pool filtré est insuffisant** — retourne moins que demandé, l'appelant décide), `DemarrerRound` (tire Cible comme BonusRoundService, génère options Qcm via `PoolPourQcm` — interne, réutilisé par BonusRoundService), `SoumettreReponse` (rejette pause/déjà répondu, calcule via `IScoringService` + `IAnswerMatcher`), `TerminerParTimeout`, `ValiderManuellement` (recalcule à partir de `PointsEnJeu` figé, applique le delta).
- `FiltrerParTagsOuGenres` exclut toujours les morceaux "disney" d'un thème autre que "disney" lui-même (retour utilisateur : univers musical trop particulier pour se mélanger).
- Duplique `EstPremiereLettreCorrecte` (voir remarque BonusRoundService).

**Rounds/TitreVariantes.cs**
- Rôle : variantes de titre acceptées en `TapeReponse`. `Acceptables(titre)` : titre complet, titre sans parenthèses, variante tronquée aux mots entiers (si > 20 caractères, `SeuilCaracteresTronque`). `EstEligibleCommeCible(titre)` : exclut du tirage Cible=Titre tout titre > 35 caractères (`SeuilCaracteresExclusion`), même tronqué trop dur à deviner à l'oreille.

**Scoring/BonusScoringService.cs** (+ interface)
- Rôle : scoring bonus, trivial. `PalierParDefautIndex => 0`, `ValeurPalier(config, index)`, `PointsResultat(mise, correct) => correct ? mise : -mise`.

**Scoring/ScoringService.cs** (+ interface)
- Rôle : formule de scoring dégressif du round classique. `CalculerPointsEnJeu` : `ratio = clamp((tempsEcoule - dureeEnPause) / dureeFenetre, 0, 1)`, `points = max(min, max - ratio*(max-min))`. `PointsBonneReponse(p) => p`. `PointsMauvaiseReponse(p, config) => -round(p * ratio)`. `PointsAbsenceReponse(config) => config.PenaliteAbsenceReponse` (fixe).

**Sessions/GameCodeGenerator.cs** (+ interface)
- Rôle : génère un code de partie à 5 caractères sans ambiguïté visuelle (alphabet excluant 0/O/1/I), via `Random.Shared` sur `Span<char>` stackalloc.

**Sessions/GameSessionStore.cs** (+ interface)
- Rôle : stockage in-memory des parties actives (pas de DB). Deux `ConcurrentDictionary` : `_sessions` (code → GameSession) et `_connexions` (ConnectionId SignalR → code de partie, car les méthodes du hub ne prennent pas `code` en paramètre). Aucune persistance — perdu au redémarrage du process (voir Program.cs `/api/admin/restart`).

### Blindify.Infrastructure

Accès disque (tracks.json, stats.json), référence Domain. C'est le plus petit et le plus simple des 4 projets — peu de surface pour un refactor.

**Configuration/DataPathsOptions.cs**
- Rôle : options bindées depuis la section `"Data"` de la config (`TracksPath`, `StatsPath`, `AudioPath?`, `CoversPath?`, `RootPath` — racine servie en `/files`).

**DependencyInjection/ServiceCollectionExtensions.cs** — Enregistre `DataPathsOptions` (bind config) + `ITracksRepository`/`IStatsRepository` en Singleton.

**Stats/IStatsRepository.cs** / **StatsEntryDto.cs** / **StatsRepository.cs**
- Rôle : persistance des compteurs runtime (`playCount`) dans `stats.json`, séparé de `tracks.json` pour ne jamais entrer en collision d'écriture avec le script d'import CSV (le backend est le seul écrivain de `stats.json`, jamais de `tracks.json`).
- Implémentation : dictionnaire en mémoire chargé au démarrage (silencieux si fichier absent), `lock (_lock)` autour de get/increment, réécriture complète du fichier JSON à chaque incrément (`File.WriteAllText`, pas de write asynchrone/batché).
- Remarque : écriture synchrone bloquante à chaque round démarré (via `IncrementPlayCount`) — potentiel point de contention si beaucoup de parties simultanées, mais volume/fréquence largement dans la marge pour un usage familial local.

**Tracks/ITracksRepository.cs** / **TrackDto.cs** / **TrackMapper.cs** / **TracksRepository.cs**
- Rôle : chargement de `tracks.json` en mémoire au démarrage (source de vérité unique, jamais réécrite par le backend — monté `:ro` en Docker). `TracksRepository` lève `FileNotFoundException`/`InvalidOperationException` si le fichier est absent/invalide au démarrage (fail-fast). `TrackMapper.ToDomain()` : mapping DTO → entité 1:1, `internal static`. `GetAll()`/`GetById(id)` — lecture seule, dictionnaire construit une fois.

### Blindify.Api

**Program.cs**
- Rôle : composition root ASP.NET Core. Enregistre Application+Infrastructure, `RoundTimerCoordinator`/`BonusTimerCoordinator` en Singleton, SignalR avec JSON camelCase + enums en string. CORS ouvert (`AllowAnyOrigin`, réseau local uniquement, pas de credentials). Sert `Data:RootPath` en fichiers statiques sous `/files` (audio/covers, host uniquement). Sert optionnellement `Host:StaticPath` (dossier `host/`) à la racine — avec mapping `.apk` → `application/vnd.android.package-archive` pour distribuer l'app Flutter aux téléphones Android sans câble. Expose `GET /api/tags` (tags distincts du catalogue, pour le sélecteur de thème du host) et `POST /api/admin/restart` (mot de passe en comparaison à temps constant, `StopApplication()` différé de 300ms pour laisser partir la réponse HTTP, 503 si mot de passe non configuré). Mappe `GameHub` sur `/hubs/game`. `public partial class Program;` en fin de fichier pour permettre `WebApplicationFactory<Program>` dans les tests.

**appsettings.json** — Logging par défaut + `Admin:RestartPassword` vide (fonctionnalité désactivée par défaut).
**appsettings.Development.json** — Chemins `Data:*` relatifs (`../../../data/...`) et `Host:StaticPath` pointant vers `../../../host` pour le dev local.

#### Contracts/ (DTOs SignalR/HTTP, tous des `record`)

- **AdminContracts.cs** — `RestartRequestDto(string Password)`.
- **BonusContracts.cs** — `BonusStakeOptionsDto`, `SelectStakeRequestDto`, `BonusQuestionStartedForHostDto` (avec audio/refrain/ralentissement), `BonusQuestionStartedForPlayersDto` (sans audio), `SubmitBonusAnswerRequestDto`, `BonusAnswerResultDto`, `BonusResultEntryDto`, `BonusResultDto` (avec `EstCourse`).
- **CreateGameContracts.cs** — `TeamDto`, `SeriesSetupDto(Config, RoundModes, Tags)`, `CreateGameRequestDto(ModeEquipe, NomsEquipes?)` (minimal, ne configure rien — retour utilisateur 2026-08-24), `CreateGameResultDto(Code, Teams, HostSecret)`, `ConfigurerPartieRequestDto(SeriesSetups, Config?)` (remplace toute la config en un appel, rappelable en Lobby).
- **HostStateSnapshotDto.cs** — Snapshot renvoyé par `RejoinAsHost` (EnPause, Mode/Cible courants, TrackId, FilePath, RefrainStartMs, PositionAudioMs calculée, DureeFenetreReponseMs).
- **JoinContracts.cs** — `PlayerSummaryDto`, `JoinGameResultDto` (inclut Teams + roster complet des Joueurs y compris soi-même), `PlayerJoinedDto`, `PlayerConnectionChangedDto`, `PlayerTeamChangedDto`.
- **RoundContracts.cs** — `QcmOptionDto(TrackId, Title, Artist, Film)`, `RoundStartedForHostDto` (avec audio), `RoundStartedForPlayersDto` (sans audio, avec SerieIndex), `SubmitAnswerRequestDto`, `RoundAnswerResultDto`, `RoundResultEntryDto`, `RoundEndedDto` (Cible/Film répétés pour l'écran de reveal).
- **ScoreContracts.cs** — `PlayerScoreDto`, `TeamScoreDto`, `ScoreUpdateDto(Joueurs, Equipes?)` (Equipes null si mode individuel).
- **SeriesContracts.cs** — `SerieAnnonceeDto(SerieIndex, Tags)`.
- **ValidateAnswerManuallyRequestDto.cs** — `ValidateAnswerManuallyRequestDto(PlayerId, EstCorrecte)`.

Remarque transverse : chaque event a une paire Host/Players distincte quand l'audio ou le TrackId de la bonne réponse doit rester caché aux joueurs (RoundStarted, BonusQuestionStarted) — pattern cohérent et répété plusieurs fois, potentiellement factorisable (les deux DTOs partagent presque tous leurs champs sauf FilePath/RefrainStartMs), mais séparé volontairement pour ne jamais risquer de fuiter l'audio/la réponse côté client joueur par erreur de sérialisation partagée.

#### Hubs/ — cœur de l'orchestration temps réel

**GameHub.cs** (~530 lignes, le plus gros fichier du projet)
- Rôle : hub SignalR unique, toutes les méthodes exposées aux clients (host + joueurs). Structure : méthodes host (CreateGame, ConfigurerPartie, RejoinAsHost, StartRound, StartBonusRound, AnnoncerSerieCourante, NextRound, ShowLeaderboard, ValidateAnswerManually, PauseGame/ResumeGame, EndGame, RejouerPartie), méthodes joueur (JoinGame, JoinTeam, SubmitAnswer, SelectStake, SubmitBonusAnswer), cycle de connexion (OnDisconnectedAsync), aides privées (ResoudreSession/ResoudreSessionHost, AppliquerFeinteEventuelle/AppliquerFeinteTexteEventuelle — `internal static`, réutilisées par BonusTimerCoordinator —, CalculerTempsEcouleMs).
- Points clés d'implémentation :
  - `CreateGame` ne configure plus rien (juste lobby+équipes+hostSecret) ; `ConfigurerPartie` sélectionne les morceaux et est rappelable tant que `Etat == Lobby`, remplace tout à chaque appel.
  - `RejoinAsHost` vérifie le `HostSecret`, recalcule la position audio théorique pour resynchroniser sans redémarrer le morceau.
  - `StartRound`/`StartBonusRound` construisent les DTOs Host et Players séparément, appliquent les feintes QCM, démarrent le timer coordinator correspondant.
  - `NextRound` avance round ou série ; si dernière série épuisée, avance quand même l'index hors limites pour que `StartRound` refuse plutôt que de relancer silencieusement le dernier round.
  - `RejouerPartie` : uniquement si `Termine`, retire toute la sélection de morceaux avec les mêmes configs de série, remet les scores à zéro, repasse en `Lobby`, garde le même code/mêmes joueurs.
  - Toute méthode host passe par `ResoudreSessionHost` qui vérifie `HostConnectionId == Context.ConnectionId` — pas de vérification de `HostSecret` ici (seulement à `RejoinAsHost`), la protection normale repose sur le fait qu'une seule connexion à la fois porte `HostConnectionId`.
  - Pas de verrouillage explicite (lock/mutex) sur `GameSession` malgré des accès concurrents possibles (timers de fond + appels hub simultanés) — repose sur le fait que SignalR traite les invocations d'un même hub de façon séquentielle par connexion, mais deux connexions différentes (host + joueurs, ou plusieurs joueurs) peuvent muter `session.Players`/`round.Reponses` en parallèle sans synchronisation explicite. Risque de race condition réel mais a priori à faible probabilité d'impact vu le volume (quelques joueurs, réseau local) — à surveiller si la refonte change le modèle de concurrence.

**RoundTimerCoordinator.cs**
- Rôle : surveille la fin d'un round classique par polling (`Task.Delay(250ms)` en boucle, `IHubContext<GameHub>` car hors cycle de vie d'un appel hub). `ConcurrentDictionary<string, CancellationTokenSource>` par code de partie. `DemarrerSurveillance`/`Annuler`. À expiration : `roundService.TerminerParTimeout` + diffuse `RoundEnded` + `ScoreUpdate`.

**BonusTimerCoordinator.cs**
- Rôle : même pattern de polling, enchaîne les deux phases (mise puis question) via `AttendreFinPhaseAsync` générique (accepte un sélecteur de date de début + une clause de fin anticipée pour le mode course). Reconstruit les mêmes DTOs Qcm que `GameHub.StartRound`, réutilise `GameHub.AppliquerFeinteEventuelle`/`AppliquerFeinteTexteEventuelle` (couplage statique explicite vers GameHub).

**ScoreDtoBuilder.cs**
- Rôle : construit `ScoreUpdateDto` à partir d'une `GameSession` (`internal static`, appelé depuis GameHub et les deux TimerCoordinators). Inclut `Equipes` seulement si `session.ModeEquipe`.

Remarque : les deux TimerCoordinators dupliquent quasi intégralement leur squelette (dictionnaire de CTS, `DemarrerSurveillance`/`Annuler`, boucle de polling 250ms, gestion `OperationCanceledException`) — bon candidat à une classe de base ou un helper générique `PollingTimerCoordinator<TState>` lors d'une refonte.

### Backend/tests/Blindify.Tests

**Hubs/GameHubTestFactory.cs** — `WebApplicationFactory<Program>` de test, catalogue de test fixe (4 morceaux Disney, dont `t1` avec `TrapTextArtist`), fichiers temporaires pour tracks/stats/root, `Host:StaticPath` vidé explicitement par défaut. `CreateHubConnection()` avec LongPolling + enums en string (miroir du protocole serveur).

**Hubs/StaticFilesTests.cs** — 4 tests : fichier audio existant/inexistant sous `/files`, racine 404 sans `Host:StaticPath`, racine sert `index.html`/`display.html` quand configuré.

**Hubs/AdminRestartTests.cs** — 3 tests : 503 sans mot de passe configuré, 401 mauvais mot de passe, 200 bon mot de passe.

**Hubs/GameHubIntegrationTests.cs** (~510 lignes, le plus gros fichier de test) — 13 tests bout-en-bout via connexions SignalR réelles (host + joueur(s)) : partie complète création→réponse→fin de round, refus de `StartRound` après épuisement de série, diffusion de `AnnoncerSerieCourante`, refus de reconfiguration après démarrage, refus de `StartRound` sans configuration, remplacement complet par rappel de `ConfigurerPartie`, `RejouerPartie` (reset scores + relance), mode équipe (agrégation de score), feinte champ croisé à probabilité 1, feinte texte inventé à probabilité 1 (boucle jusqu'à tomber sur `t1`+Auteur, jusqu'à 200 tentatives — test intrinsèquement dépendant du hasard, mitigé par une grande marge de tentatives), roster complet à la connexion, code inconnu, pause/reprise avec rejet des réponses pendant la pause, `RejoinAsHost` avec vérification du secret + resynchronisation, lobby sans configuration ne crashe pas `RejoinAsHost`.

**Hubs/GameHubBonusIntegrationTests.cs** — 1 test bout-en-bout complet du cycle bonus (mise explicite + palier par défaut, résultat correct/incorrect selon le Mode tiré au hasard — gère les 3 modes possibles dynamiquement plutôt que de figer une assertion).

**Answers/AnswerMatcherTests.cs** — Levenshtein (cas connus), normalisation (accents/casse/ponctuation), `EstCorrecte` sur les 3 zones de tolérance (exact courte, tolérance fixe intermédiaire, ratio au-delà), avec les cas de régression documentés ("Xo"/"Go", "Hayley"/"Halsey").

**Bonus/BonusRoundServiceTests.cs** (~450 lignes) — Couverture large : mise (première fois, doublon, après début phase question, pendant pause), palier par défaut, réponse correcte/incorrecte/sans mise, timeout, cible Film pour disney, tirage 50/50 Titre/Auteur, exclusion Titre si trop long, comparaison au nom du film (pas au titre réel), variante parenthèses, tirage des 3 modes sur 300 essais, génération Qcm en mode bonus, comparaison par ID en Qcm, PremiereLettre, et toute la mécanique "course" (2e réponse ignorée, timeout ne pénalise pas les autres si quelqu'un a répondu, pénalise tout le monde si personne n'a répondu).

**Qcm/QcmGeneratorTests.cs** — Couvre chaque palier du pipeline : 4 options avec bonne réponse, repli pool global si genre/tag insuffisant, évitement auteur dupliqué (avec un cas où le catalogue restreint force quand même la duplication plutôt que de bloquer), évitement doublon entre deux distracteurs, repli année proche (test précis vérifiant l'ensemble exact des IDs retenus), piège prioritaire à probabilité 1, piège exclu s'il partage l'auteur du morceau correct, genre trop générique insuffisant seul comme ancre, plusieurs ancres possibles → distracteurs tous de la même ancre, ancre partagée par 2 langues → pas de mélange francophone/anglophone.

**Rounds/FilmNameResolverTests.cs** — Couverture par `[Theory]` de tous les formats connus (anglais D23, français VF, extrait de titre prioritaire sur album de compilation), cas non résolvable (best-effort documenté), album absent.

**Rounds/RoundServiceTests.cs** (~480 lignes) — Sélection sans répétition, respect de `dejaUtilises` entre deux appels, pas de repli hors thème si pool insuffisant, exclusion Disney d'un thème générique même si le tag correspond, pondération par `playCount` (test statistique sur 500 tirages, >90% sur le jamais-joué), thème Disney explicite inclut les morceaux Disney, génération Qcm, absence d'options en TapeReponse, scoring bonne/mauvaise réponse, PremiereLettre (casse, accents, incorrecte), tirage de Cible (50/50 sur 50 essais, toujours Film pour Disney), pool Qcm restreint à Disney pour cible Film, comparaison au nom du film nettoyé, variantes titre avec/sans parenthèses, Auteur multiple (un seul suffit) et son PremiereLettre, rejet double soumission, rejet pendant pause, timeout ne pénalise que les non-répondants, `ValiderManuellement` (bascule faux→juste avec delta, joueur sans réponse retourne null).

**Rounds/TitreVariantesTests.cs** — Titre court = titre complet seul, titre long contient aussi la variante tronquée, `EstEligibleCommeCible` selon la longueur (3 cas).

**Scoring/BonusScoringServiceTests.cs** — 4 tests triviaux (gain/perte de mise, valeur de palier, palier par défaut = safe).

**Scoring/ScoringServiceTests.cs** — Formule dégressive (début = max, fin de fenêtre = min, au-delà toujours plafonné au min, neutralisation par la durée de pause avec calcul exact vérifié), pénalité mauvaise réponse = moitié en valeur absolue, pénalité absence = valeur fixe configurée.

**Sessions/GameCodeGeneratorTests.cs** — Longueur 5, absence de caractères ambigus (0/O/1/I), génère des codes différents.

**Sessions/GameSessionStoreTests.cs** — Add/Get, code inconnu, Exists, Remove — CRUD basique sur le store en mémoire.

**Stats/StatsRepositoryTests.cs** — Fichier inexistant → 0, incrément simple, incréments multiples cumulés, persistance disque entre deux instances, isolation entre IDs différents.

**Tracks/TracksRepositoryTests.cs** — Fichier introuvable → exception, chargement/mapping complet depuis JSON, ID inconnu → null.

### Vue d'ensemble backend

Le point de couplage le plus fort et le plus visible est `GameHub` ↔ `RoundTimerCoordinator`/`BonusTimerCoordinator` : les deux coordinators reconstruisent une partie de la logique de diffusion de `GameHub` (mêmes DTOs, appellent `GameHub.AppliquerFeinteEventuelle`/`AppliquerFeinteTexteEventuelle` en statique) car ils tournent hors du cycle de vie d'un appel hub et utilisent `IHubContext` plutôt que `Clients`. Cette architecture par polling à 250ms (deux coordinators quasi identiques dans leur squelette) est le principal candidat à simplification structurelle — soit en fusionnant en un seul coordinator générique paramétré par phase, soit en migrant vers une approche event-driven/timer unique si le volume de parties simultanées grossissait.

Observations générales :
1. **Duplication assumée et documentée** : `EstPremiereLettreCorrecte` existe identiquement dans `RoundService` et `BonusRoundService` (commentaire explicite justifiant le choix plutôt qu'un partage), et le squelette de polling est dupliqué entre les deux TimerCoordinators sans commentaire équivalent.
2. **Pas de verrou explicite sur `GameSession`** malgré des mutations concurrentes possibles (plusieurs connexions SignalR simultanées + timers de fond) — fonctionne dans la pratique (faible charge, réseau local familial) mais fragile si le modèle de concurrence devait changer.
3. **`GameHub.cs` (530 lignes) concentre beaucoup de responsabilités** : orchestration de toutes les méthodes host/joueur, construction de DTOs, application des feintes QCM (logique métier qui pourrait vivre dans Application), résolution de session — un bon candidat à extraction partielle (ex. la construction des DTOs RoundStarted/BonusQuestionStarted pourrait devenir un `RoundStartedDtoBuilder` symétrique à `ScoreDtoBuilder`).
4. **Aucune persistance de l'état de partie** (`GameSessionStore` en mémoire pure) — cohérent avec CLAUDE.md ("pas de DB") mais signifie qu'un redémarrage backend (y compris via `/api/admin/restart`) perd toute partie en cours, ce qui est un choix de conception explicite et assumé (avertissement affiché côté host avant confirmation).
5. **`QcmGenerator` (247 lignes) est le fichier le plus dense en règles métier fines** (ancre unique, barrière francophone, seuil relatif de généricité, 3 paliers de repli + filet de sécurité) — bien testé (10 tests dédiés) mais sa complexité progressive (accumulation de retours utilisateur successifs) en fait un point sensible pour toute refonte du système de distracteurs.
6. **Couplage statique `GameHub` → `BonusTimerCoordinator`** via les deux méthodes `internal static AppliquerFeinte*` : fonctionnel mais un peu inhabituel comme mécanisme de partage de logique entre deux classes du même namespace plutôt qu'un service dédié injecté.

---

## 4. Inventaire fichier par fichier — App Flutter (`app/lib/`, client joueur)

33 fichiers, ~3300 lignes. Un seul `ChangeNotifier` (`GameConnection`) possède tout l'état ; navigation pilotée par un enum `AppScreen` plutôt que par des routes Navigator — le client est purement réactif aux événements SignalR reçus (jamais de bouton "suivant" local qui avance l'état de jeu, sauf actions joueur explicites comme répondre).

### Racine

**`main.dart`** (162 l.)
- Rôle : point d'entrée, force l'orientation portrait, monte `GameConnection` via `ChangeNotifierProvider`, et route vers l'écran courant.
- Contenu clé : `BlindifyApp` (MaterialApp + provider) ; `_RootScreen` — un `switch` exhaustif sur `AppScreen` (enum défini dans `game_connection.dart`) qui choisit le widget à afficher, enveloppé dans un `AnimatedSwitcher` (fade 320ms) ; header fixe "BLINDIFY" + `_ConnectionPill` (badge connecté/déconnecté) ; overlay `LeaderboardOverlay` affiché par-dessus si `game.showLeaderboard`.
- Dépendances notables : tous les écrans, `GameConnection`, `theme.dart`.
- Remarques : la navigation n'utilise pas `Navigator`/routes nommées — c'est un `switch` piloté par un seul enum d'état global, en miroir volontaire (commenté) du pattern déjà utilisé côté host (`host/app.js`). Fonctionne bien pour un flow linéaire mais rend impossible toute navigation arrière ou pile d'écrans (compensé ponctuellement par `Navigator.push` pour `QrScanScreen`, qui est un sous-flux local hors `AppScreen`).

**`theme.dart`** (147 l.)
- Rôle : thème Material 3 unique de l'app + palette de couleurs partagée.
- Contenu clé : `BlindifyColors` (palette figée, dupliquée intentionnellement avec `host/style.css` — commentaire explicite "identité affiche de concert" des deux côtés) ; `buildBlindifyTheme()` configure toutes les theme extensions (AppBar, Card, Input, boutons, Chip, texte) ; `hardShadow()` — helper d'ombre dure décalée réutilisé partout.
- Dépendances notables : `google_fonts` (Anton, Space Grotesk, Space Mono).
- Remarques : la duplication de palette avec le CSS host est un couplage intentionnel mais fragile — un changement de couleur doit être répliqué à la main dans deux langages/fichiers sans mécanisme de synchronisation.

### `models/` (12 fichiers)

Tous suivent le même schéma : classe immuable + `factory X.fromJson(Map<String, dynamic>)`, aucune logique métier sauf deux exceptions notées ci-dessous.

**`bonus_question_started.dart`** (44 l.) — Rôle : miroir de `BonusQuestionStartedForPlayersDto`. Contenu clé : `BonusQuestionStarted` — `dureePhaseQuestionMs`, `cible` (`RoundCible`), `serieIndex`, `mode` (`RoundMode`), `qcmOptions` (optionnel), `estCourse` (bool, défaut false). Jamais de champ audio. Dépendances : `qcm_option.dart`, `round_cible.dart`, `round_mode.dart`.

**`bonus_result.dart`** (74 l.) — Rôle : résultat de la question bonus. Contenu clé : `BonusResultEntry` (playerId, mise, reponse, estCorrecte, points) ; `BonusResult` (trackId, title, artist, coverPath, cible, film, resultats, estCourse) avec un getter `reponseAttendue` (switch sur `cible` : Film→film, Auteur→artist, sinon→title). Remarques : le getter `reponseAttendue` duplique une logique quasi identique à celle de `round_ended.dart` — deux implémentations légèrement différentes du même besoin de présentation.

**`bonus_stake_options.dart`** (18 l.) — Rôle : miroir de `BonusStakeOptionsDto` — les 4 paliers de mise annoncés à l'aveugle. Contenu clé : `BonusStakeOptions` — `paliers` (`List<int>`), `dureePhaseMiseMs`, `serieIndex`.

**`join_result.dart`** (50 l.) — Rôle : réponse de `JoinGame`. Contenu clé : `PlayerSummary` (playerId, nom, estConnecte, teamId) ; `JoinResult` (success, errorMessage, score, teamId, teams, joueurs — le roster complet de la partie, soi-même inclus). Dépendances : `team.dart`.

**`qcm_option.dart`** (17 l.) — Rôle : une option de QCM. Contenu clé : `QcmOption` — trackId, title, artist, film (nom du film d'origine pour la cible Film).

**`round_cible.dart`** (29 l.) — Rôle : miroir de `Blindify.Domain.Enums.RoundCible`. Contenu clé : `enum RoundCible { titre, auteur, film }` ; extension `RoundCibleJson` avec `fromJson` (parsing strict, lève `ArgumentError` sur valeur inconnue) et `label` (texte affiché : "le titre"/"l'artiste"/"le film").

**`round_ended.dart`** (62 l.) — Rôle : résultat d'un round classique. Contenu clé : `RoundResultEntry` (playerId, reponse, estCorrecte, points) ; `RoundEnded` (trackId, title, artist, coverPath, cible, film, resultats) avec un getter `reponseAttendue` (cible=='Film' ? film : title — version simplifiée sans cas "Auteur", contrairement à `BonusResult`). Remarques : duplication partielle avec `BonusResult.reponseAttendue` — candidat à factoriser dans un mixin/fonction partagée.

**`round_mode.dart`** (28 l.) — Rôle : miroir de `Blindify.Domain.Enums.RoundMode`. Contenu clé : `enum RoundMode { qcm, tapeReponse, premiereLettre }` ; extension avec `fromJson` et `label` ("QCM"/"Réponse tapée"/"Première lettre").

**`round_started.dart`** (36 l.) — Rôle : miroir de `RoundStartedForPlayersDto`. Contenu clé : `RoundStarted` — mode, cible, dureeFenetreReponseMs, serieIndex (0-based, pour "Série A/B/C..."), qcmOptions. Dépendances : `qcm_option.dart`, `round_cible.dart`, `round_mode.dart`. Remarques : jamais de champ audio, contrairement à `RoundStartedForHostDto` côté serveur — cohérent avec la règle "l'audio ne sort jamais vers les joueurs".

**`score_update.dart`** (47 l.) — Rôle : miroir de `ScoreUpdateDto`. Contenu clé : `PlayerScore` (playerId, nom, score, teamId) ; `TeamScore` (teamId, nom, score) ; `ScoreUpdate` (joueurs, equipes optionnel — présent seulement si mode équipe actif).

**`serie_annoncee.dart`** (14 l.) — Rôle : miroir de `SerieAnnonceeDto`. Contenu clé : `SerieAnnoncee` — serieIndex, tags (`List<String>`).

**`team.dart`** (11 l.) — Rôle : équipe minimale. Contenu clé : `Team` — id, nom. Le plus petit fichier du module.

### `services/game_connection.dart` (495 l.) — fichier central

- Rôle : unique point de communication SignalR (`HubConnection`), source de vérité de tout l'état runtime (écran courant, joueurs, scores, round/bonus en cours), exposé comme `ChangeNotifier` unique consommé via `provider` dans tous les écrans.
- Contenu clé :
  - `enum AppScreen` (12 valeurs : loading, connect, join, lobby, serieIntro, round, roundEnded, bonusStake, bonusCourseIntro, bonusQuestion, bonusResult, ended) et `class PlayerInfo`.
  - **Identité persistante** : `init()` charge/génère un `playerId` (16 octets aléatoires en hex via `Random.secure()`) stocké via `shared_preferences`, jamais régénéré — clé de reconnexion côté serveur (`JoinGame`).
  - **Connexion** : `connect(url, {timeout, silent})` — ferme une connexion existante avant d'en ouvrir une nouvelle, construit un `HubConnection` avec `withAutomaticReconnect()`, gère un timeout optionnel (reconnexion auto au démarrage bornée à 4s via `_delaiReconnexionAuto`) et un mode "silencieux" (pas de message d'erreur si l'utilisateur n'a rien déclenché explicitement).
  - **`_registerHandlers()`** — enregistre tous les handlers d'événements du contrat SignalR (`PlayerJoined`, `PlayerReconnected/Disconnected`, `PlayerTeamChanged`, `SerieAnnoncee`, `RoundStarted`, `ScoreUpdate`, `RoundEnded`, `GamePaused/Resumed`, `LeaderboardShown`, `GameEnded`, `GameRestarted`, `BonusStakeOptions`, `BonusQuestionStarted`, `BonusResult`), chacun mettant à jour l'état local puis appelant `notifyListeners()` — c'est directement `screen = AppScreen.xxx` qui pilote la navigation.
  - **Reconnexion automatique** (`onreconnected`) : rejoue `JoinGame` avec le `playerId` stable pour que le serveur réassocie le joueur existant (best-effort, erreur avalée silencieusement).
  - **Mode course** : logique dédiée dans le handler `BonusQuestionStarted` — bascule sur un écran forcé (`bonusCourseIntro`) pendant `_dureeIntroCourse` (2500ms) avant de révéler la vraie question, avec garde contre une race (`BonusResult` pouvant arriver avant la fin du timer si un autre joueur répond très vite — vérifie `screen == AppScreen.bonusCourseIntro` avant de basculer).
  - **Actions joueur** : `joinGame`, `joinTeam`, `submitAnswer`, `selectStake`, `submitBonusAnswer` — chacune applique une garde locale anti-double-soumission (`roundAnswered`/`bonusStakeEnvoyee`/`bonusAnswered`) en plus de la garde serveur, avec mise à jour optimiste de l'UI avant confirmation.
  - `coverUrl(String? coverPath)` — construit l'URL `$serverUrl/files/$coverPath`.
  - `_syncOwnScore()` — extrait son propre score/équipe depuis un `ScoreUpdate` reçu.
  - `dispose()` — annule le timer et arrête le hub.
- Dépendances notables : la quasi-totalité des modèles, packages `signalr_netcore`, `shared_preferences`.
- Remarques : c'est un God Object assumé (commentaire explicite dans le code : "état global simple... plutôt qu'un Navigator", mirroring du pattern host) — un seul fichier concentre transport réseau, persistance locale, état de toutes les phases de jeu (lobby/round/bonus/leaderboard) et logique de navigation. Fonctionnel et lisible pour la taille actuelle du projet, mais c'est le premier candidat à un découpage (ex : séparer connexion SignalR / état de partie / état de round-en-cours) si la refonte vise la testabilité ou l'ajout de nouveaux modes de jeu. Aucun test unitaire visible pour ce fichier.

### `screens/` (13 fichiers)

**`loading_screen.dart`** (42 l.) — Rôle : affiché pendant la tentative de reconnexion auto au démarrage. StatelessWidget — spinner + "Connexion en cours..." + `serverUrl` affiché s'il existe déjà. Aucune action possible sur cet écran, disparaît dès que la connexion réussit ou échoue.

**`connect_screen.dart`** (121 l.) — Rôle : saisie manuelle de l'URL serveur. StatefulWidget — `TextEditingController` pré-rempli avec `serverUrl` connu ; bouton "Se connecter" (spinner pendant `connecting`) ; bouton "Scanner le QR" (`Navigator.push` vers `QrScanScreen`) ; bouton conditionnel Android (`!kIsWeb && defaultTargetPlatform == TargetPlatform.android`) "Mettre à jour l'app" → `url_launcher` ouvre `$base/blindify.apk`. Remarques : détection de plateforme en dur dans l'écran plutôt qu'abstraite dans un service.

**`join_screen.dart`** (134 l.) — Rôle : saisie pseudo + code de partie. StatefulWidget — deux `TextEditingController` (nom, code) ; consomme `game.pendingJoinCode` (pré-rempli après un scan QR) à la fois dans `initState` ET dans `build` (pour couvrir le cas où l'écran est déjà monté au moment du scan) ; `_join()` appelle `game.joinGame(code, nom)` avec validation locale ; bouton "Scanner le QR". Remarques : double consommation de `pendingJoinCode` documentée en commentaire — logique correcte mais un peu subtile.

**`qr_scan_screen.dart`** (154 l.) — Rôle : scan du QR affiché sur l'écran public host (payload JSON `{"server","code"}`), poussé en modal (`Navigator.push`) — pas un `AppScreen` dédié, sous-flux local à `ConnectScreen`/`JoinScreen`. Contenu clé : `MobileScannerController`, `_onDetect` parse le JSON (try/catch silencieux sur QR malformé/autre appli), deux chemins : si déjà connecté au même serveur → `setPendingJoinCode` + pop immédiat (pas de reconnexion) ; sinon → `game.connect(server)` complet puis pop si succès. `_ScanFrame` — cadre de visée décoratif. Bouton lampe torche.

**`lobby_screen.dart`** (129 l.) — Rôle : salle d'attente. StatelessWidget — affiche le code de partie (gros, espacé) ; sélecteur d'équipe (`Wrap` de `ChoiceChip`, visible seulement si `game.teams` non vide, appelle `joinTeam`) ; liste des joueurs connectés (`PlayerAvatar` + nom + équipe/statut déconnecté).

**`serie_intro_screen.dart`** (41 l.) — Rôle : annonce "Série X — thème" avant le premier round d'une série. Utilise `lettreSerie()`/`libelleTheme()` (de `serie_badge.dart`). Pas de minuteur local — reste affiché jusqu'au prochain événement serveur (`RoundStarted` ou `BonusStakeOptions`).

**`round_screen.dart`** (316 l.) — Rôle : écran de jeu principal (round classique). StatefulWidget — `TextEditingController` pour la saisie texte, ticker local (`Timer.periodic` 100ms) synchronisé sur la pause (`_syncTickerWithPause`, idempotent) ; `_autoSubmitSiSaisie()` valide automatiquement une saisie texte déjà tapée ~1s avant expiration, uniquement en mode `tapeReponse` ; affiche `MysteryCoverArt`, `SerieBadge`, `TimerBar`, bannière pause/réponse envoyée ; délègue le rendu de la zone de réponse à 3 sous-widgets privés selon `round.mode` : `_QcmAnswers` (liste de boutons, un seul champ affiché selon `round.cible`), `_AnswerTile` (bouton générique), `_LetterAnswer` (grille A-Z 5 colonnes, `childAspectRatio` calculé dynamiquement), `_TextAnswer` (TextField + bouton), `_Banner` (bannière pause/réponse envoyée, widget privé dupliqué dans plusieurs fichiers). Remarques : **quasi-duplication complète** avec `bonus_question_screen.dart` (mêmes 3 sous-widgets de réponse, même logique de ticker/pause/auto-submit, même `_Banner`) — candidat fort à factorisation.

**`round_ended_screen.dart`** (70 l.) — Rôle : révélation résultat round classique. `CoverArt` (image révélée), réponse attendue (`result.reponseAttendue`), si cible==Film affiche aussi titre+artiste réels en sous-texte (trivia), sinon juste l'artiste ; icône/texte correct-faux + points pour son propre résultat.

**`bonus_stake_screen.dart`** (153 l.) — Rôle : phase 1 de la question bonus — mise à l'aveugle. StatefulWidget — ticker local (même pattern idempotent que `RoundScreen`), liste des 4 paliers cliquables (`Material`/`InkWell`, mise en évidence du palier sélectionné), bannière pause/mise envoyée, `_Banner` (encore une copie locale — 3ᵉ copie du widget dans le module).

**`bonus_course_intro_screen.dart`** (42 l., **non tracké git**) — Rôle : écran forcé ~2,5s expliquant la règle du mode "course" avant la vraie question bonus, quand `estCourse == true` (piloté par `GameConnection._introCourseTimer`). Contenu : icône éclair, titre "MODE COURSE", explication en 2 lignes. Pas de logique propre — purement statique. Nouveau fichier, correspond à la fonctionnalité "course" récemment ajoutée.

**`bonus_question_screen.dart`** (319 l.) — Rôle : phase 2 de la question bonus — devinette. StatefulWidget — même structure que `RoundScreen` : ticker avec marge d'auto-submit, `_syncTickerWithPause`, `MysteryCoverArt`, `SerieBadge`, `TimerBar`, bannière spéciale mode course (`🏁 Course !...`) affichée seulement si `estCourse && !bonusAnswered && !paused`, puis délègue à `_QcmAnswers`/`_LetterAnswer`/`_TextAnswer`/`_AnswerTile`/`_Banner` (copies quasi identiques à celles de `RoundScreen`). Fichier le plus long de `screens/` avec `round_screen.dart`.

**`bonus_result_screen.dart`** (71 l.) — Rôle : révélation résultat de la question bonus. `CoverArt`, réponse attendue, sous-texte contextuel selon la cible ; si `monResultat` existe → icône/points/mise ; sinon si `result.estCourse` → message "un autre joueur a répondu en premier, mise récupérée intacte" (cas où le joueur n'apparaît pas dans `resultats`). Gère correctement ce cas particulier.

**`leaderboard_overlay.dart`** (108 l.) — Rôle : overlay modal (pas un `AppScreen`, affiché par-dessus via `Stack` dans `main.dart`). `_ScoreList` (joueurs triés par score + section "ÉQUIPES" si applicable), `_ScoreRow` (avatar + nom + score), bouton "Fermer".

**`ended_screen.dart`** (81 l.) — Rôle : classement final. Joueurs triés par score, top 3 mis en valeur (couleur médaille + bordure/ombre plus marquée), `PlayerAvatar` avec médaille pour les 3 premiers.

### `widgets/` (5 fichiers)

**`cover_art.dart`** (122 l.) — Rôle : deux widgets liés à la pochette d'album. `CoverArt` (image réseau avec fallback) et `MysteryCoverArt` (StatefulWidget, `SingleTickerProviderStateMixin` — disque vinyle animé en rotation continue, dessiné via `_VinylPainter`/`CustomPainter` : sillons concentriques, label central, plutôt qu'un dégradé CSS comme côté host). Le joueur ne reçoit jamais `coverPath` avant le reveal — `MysteryCoverArt` matérialise ça visuellement.

**`game_card.dart`** (26 l.) — Rôle : conteneur "carte scène" générique, wrapper racine de presque tous les écrans. `Container` avec bordure épaisse + ombre dure décalée (`hardShadow`). Même esprit que `.screen` côté `host/style.css`.

**`player_avatar.dart`** (67 l.) — Rôle : avatar rond coloré par identifiant, ou médaille pour le podium. `_paletteAvatars` (12 couleurs fixes, identique à `host/app.js:couleurAvatar` et `display.js`) ; `_couleur()` base la couleur sur la position dans `game.players`/`game.teams` (ordre d'arrivée) plutôt qu'un hash, avec repli hash HSL si l'id n'est trouvé nulle part ; si `medaille` fourni et < 3, affiche 🥇🥈🥉. Remarques : couplage à `GameConnection` juste pour connaître la position dans le roster — pourrait recevoir cette info en paramètre.

**`serie_badge.dart`** (40 l.) — Rôle : mélange un badge visuel et des fonctions utilitaires de formatage de texte. `lettreSerie(int index)` (A/B/C... retombe sur un numéro au-delà de Z) ; `libelleTheme(List<String> tags)` (équivalent Dart de `host/app.js:libelleTheme`) ; `SerieBadge` (badge pill "Série X"). Remarques : deux responsabilités sans rapport direct dans le même fichier — mineur.

**`timer_bar.dart`** (58 l.) — Rôle : barre de progression + décompte chiffré, partagée par tous les écrans à minuteur. Couleur dégressive selon `progress` (accent → warn ≤40% → bad ≤15%) via `TweenAnimationBuilder<Color?>` pour une transition douce.

### Vue d'ensemble app Flutter

**Gestion d'état** : un seul `ChangeNotifier` (`GameConnection`) possède tout — connexion réseau, identité joueur, roster, scores, état du round/bonus en cours, et l'écran actif (`AppScreen` enum). Tous les écrans le consomment via `provider`, aucun état local persistant en dehors des `TextEditingController`/tickers propres à chaque écran (proprement disposés). Pattern simple et cohérent, assumé comme un mirroring volontaire de l'approche déjà validée côté host (état global piloté par les événements serveur plutôt qu'un state management plus élaboré type Bloc/Riverpod).

**Flow de navigation** (dans l'ordre d'une partie) : `loading` → (échec/pas d'URL connue) `connect` → `join` → `lobby` → `serieIntro` → `round` (×N) → `roundEnded` → retour à `round` ou `serieIntro` selon le prochain événement serveur → en fin de série : `bonusStake` → (`bonusCourseIntro` optionnel si QCM+course) → `bonusQuestion` → `bonusResult` → nouvelle `serieIntro` ou `ended`. La navigation n'est jamais déclenchée localement par un bouton "suivant" — c'est toujours la réception d'un événement SignalR qui fait avancer `screen`, le client est donc purement réactif à l'état serveur.

**Reconnexion** : gérée à deux niveaux — `HubConnectionBuilder().withAutomaticReconnect()` gère la reconnexion réseau transport, et `onreconnected` rejoue `JoinGame` avec le `playerId` stable pour que le serveur ré-associe le joueur (nouveau `connectionId`, même état).

**Duplication la plus significative** : `round_screen.dart` et `bonus_question_screen.dart` (316 et 319 lignes) sont structurellement quasi identiques — même ticker, même auto-submit, mêmes 3 widgets de réponse et le même `_Banner`, dupliqués intégralement plutôt que partagés (`bonus_stake_screen.dart` a aussi sa propre copie de `_Banner`, soit 3 copies au total). Une factorisation en un seul écran "phase de réponse" générique (paramétré par source de données, cible, callback de soumission, présence du mode course) réduirait sensiblement la surface de maintenance. Le getter `reponseAttendue` dupliqué entre `RoundEnded` et `BonusResult` va dans le même sens, à plus petite échelle.

**Observations générales** : code propre, bien commenté (beaucoup de commentaires "retour utilisateur" traçant la raison d'un choix), pas de dépendance à des générateurs de code (`json_serializable` etc. — tout le parsing JSON est écrit à la main), couplage volontaire et documenté avec le CSS/JS du host (palette de couleurs, `libelleTheme`) qui n'a aucun mécanisme de synchronisation entre les deux stacks. Aucun test unitaire du service central ou des écrans (seul `widget_test.dart` par défaut présent).

---

## 5. Inventaire fichier par fichier — Client host web (`host/`)

Vanilla JS pur, aucun framework, aucun build tooling (choix assumé pour rester servable en fichiers statiques par le backend sans étape de compilation). Deux pages HTML autonomes.

**`host/index.html`**
- Rôle : panneau de contrôle réservé à l'organisateur — écran unique multi-sections (`<section class="screen">`) togglées en JS, jamais de navigation multi-pages.
- Contenu clé : 10 écrans dans l'ordre du cycle de vie : `screen-connect` (URL serveur) → `screen-setup` (CreateGame minimal : mode équipe + noms) → `screen-lobby` (code, QR, joueurs, **bloc `#blindtest-config` imbriqué** avec tags/nombre de séries/rounds/durée/musique continue/délai, bouton "Démarrer la partie") → `screen-serie-intro` → `screen-round` → `screen-round-ended` (table résultats + override manuel) → `screen-bonus-stake` → `screen-bonus-question` → `screen-bonus-result` → `screen-ended` (scores finaux + graphique). Plus 2 overlays : `leaderboard-overlay`, `restart-overlay` (mot de passe admin). Bloc `#game-controls` (audio `<audio controls>`, pause/reprise, tableau général) est hors du flux des écrans pour rester visible peu importe l'écran actif (piloté par `ECRANS_AVEC_CONTROLES` dans JS). IDs très nombreux et tous consommés individuellement par `app.js` via `el(id)`.
- Dépendances : `vendor/signalr.min.js`, `vendor/qrcode.js`, `app.js`, `style.css`, Google Fonts.
- Remarques : couplage fort et unidirectionnel HTML→JS par ID (aucun framework, aucune indirection) — tout renommage d'ID casse silencieusement `app.js` sans erreur de compilation. `#blindtest-config` a été extrait de son propre écran suite à un retour utilisateur (`CreateGame` désormais minimal, `ConfigurerPartie` séparé et rappelable) — bon exemple de la dette que produit ce pattern quand les règles métier évoluent : la structure HTML doit être retouchée à la main en miroir du contrat SignalR.

**`host/display.html`**
- Rôle : écran public (vidéoprojecteur/TV), ouvert comme fenêtre séparée (`window.open`) depuis le panneau de contrôle. Ne se connecte jamais à SignalR.
- Contenu clé : 9 écrans, sous-ensemble volontairement épuré de ceux d'`index.html` (pas d'écran connexion/setup — reçoit tout par `postMessage`). Réutilise les mêmes conventions d'ID que `index.html` mais dans un DOM totalement séparé (ex. `screen-round`, `timer-fill`, `lobby-players` existent identiquement dans les deux fichiers — pas de partage de composants, juste duplication de nommage). Ajouts spécifiques : `screen-idle`, `paused-banner`, `round-qcm-options`/`bonus-question-qcm-options`, `btn-launch-from-display` (lancer la partie depuis l'écran public, relayé au host).
- Dépendances : `vendor/qrcode.js` (mais pas `signalr.min.js` — cohérent avec "ne se connecte jamais au serveur"), `display.js`, `style.css` + `display.css`.
- Remarques : duplication structurelle quasi complète avec `index.html` (mêmes IDs d'écrans, mêmes classes) sans aucun partage de template — toute évolution d'un écran doit être répercutée à la main dans les deux fichiers HTML *et* les deux fichiers JS.

**`host/app.js`** (≈1615 lignes — fichier central, monolithique)
- Rôle : toute la logique du panneau de contrôle — connexion SignalR, machine à états des écrans, configuration de partie, minuteurs, lecture audio, diffusion vers l'écran public, rendu de tous les composants, enchaînements automatiques.
- Contenu clé, par section :
  - **État global** (l.1-63) : ~25 variables globales mutables (`connection`, `gameCode`, `hostSecret`, `players`, timers, `equipes`, `scoreHistory`, état de l'écran public) — aucun state management, tout est lu/écrit directement par les handlers.
  - **Rendu** (l.97-463) : `renderPlayers`, `renderScoreList`, `renderScoreChart` (graphique SVG "qui menait quand" dessiné à la main, sans lib), `renderResults`/`renderBonusResults` (tables avec override manuel), `avatarHtml`/`couleurAvatar` (palette fixe de 12 couleurs, dupliquée à l'identique dans `display.js` et `app/lib/widgets/player_avatar.dart`), `renderJoinQrCodeHost` (dupliqué avec `display.js:renderJoinQrCode`).
  - **Minuteur visuel** (l.464-504) : `startTimer`/`pauseTimer`/`resumeTimer`/`stopTimer` — approximatif côté client, le serveur reste seul juge.
  - **Audio** (l.506-580) : `playAudio`, `jouerRefrain` (saut au refrain au reveal), `fadeAudioVolume` (fondus manuels via `setInterval`), gestion du blocage autoplay navigateur.
  - **Diffusion écran public** (l.582-654) : `syncDisplay()` sérialise tout l'état pertinent et le pousse par `postMessage` à `displayWindow` — canal choisi explicitement pour éviter les soucis d'origine `file://` avec `BroadcastChannel`.
  - **Handlers SignalR** (l.656-930) : `registerHandlers()` — un handler par événement du contrat — chaque handler mélange mise à jour d'état local, manipulation DOM directe et appel à `syncDisplay()`.
  - **Connexion** (l.932-979) : `tenterConnexion` avec option silencieuse/timeout (reconnexion auto au chargement).
  - **Configuration de partie** (l.983-1232) : chargement des tags (`GET /api/tags`), `pickRandomRoundModes`, répartition des thèmes par série (`assignerThemesAuxSeries`, sans répétition tant que le vivier n'est pas épuisé), calcul géométrique des paliers de mise bonus (`paliersPourSerie`, converge vers 3000 pts à la dernière série), construction des payloads `CreateGame`/`ConfigurerPartie`.
  - **Enchaînement automatique** (l.1234-1436) : logique complexe et couplée d'auto-avancement — `scheduleAutoNext`, `scheduleAutoEndGame`, `scheduleAutoNextSerie`, écran d'intro de série (`afficherIntroSerie`/`scheduleAutoStartSerie`) — plusieurs `setTimeout`/`setInterval` parallèles à annuler soigneusement pour éviter les déclenchements croisés.
  - **Redémarrage admin** (l.1494-1547) : `fetch` direct (hors SignalR) vers `POST /api/admin/restart`.
  - **Bootstrap** (l.1591-1615) : connexion auto à l'origine de la page (host servi par le backend lui-même) avec repli sur `localStorage`.
- Dépendances notables : `signalR` (global UMD), `qrcode` (global UMD), DOM natif — zéro dépendance de build.
- Remarques :
  - Fichier unique concentrant à peu près 8 responsabilités distinctes (transport SignalR, state machine d'écrans, rendu, audio, minuteurs, config de partie, auto-enchaînement, diffusion cross-fenêtre) — aucune séparation en modules malgré `"use strict"` en tête.
  - Couplage DOM très fort : quasiment chaque fonction lit/écrit des éléments via `el(id)` sans indirection — un renommage d'ID HTML casse silencieusement (pas d'erreur à la compilation, juste un `null` runtime).
  - Duplication délibérée mais non abstraite avec `display.js` : `couleurAvatar`, `COULEURS_PODIUM`, `renderScoreChart`, `libelleReveal`, `avatarHtml`, QR code — chaque fonction existe en double, synchronisée à la main (commentaires explicites "miroir de..." des deux côtés).
  - Beaucoup de logique métier vit ici alors qu'elle pourrait/devrait être côté serveur (répartition des thèmes par série, calcul des paliers de mise géométriques) — `ConfigurerPartieRequest.config` est construit entièrement côté client puis simplement rejoué tel quel par le serveur.
  - État mutable global tout au long du fichier sans structuration (pas d'objet `state` unique) — rend le raisonnement sur "qui peut modifier quoi, quand" difficile au-delà d'une lecture linéaire complète.

**`host/display.js`**
- Rôle : réception passive de l'état via `postMessage` (`appliquerEtat`), aucune connexion réseau propre, rendu miroir des écrans publics.
- Contenu clé : `appliquerEtat(msg)` — gros `switch(msg.screen)` qui bascule l'écran affiché et peuple le DOM selon le type. Minuteur local resynchronisé depuis un timestamp absolu (`timerEndAt`) transmis par le host, donc robuste à un léger délai de message. Duplique (quasi à l'identique, avec commentaires "miroir de host/app.js:...") : `couleurAvatar`, `couleurClassement`, `renderScoreChart`, `renderJoinQrCode`, `libelleReveal`, `avatarHtml`. Écoute aussi `leaderboard-show`/`leaderboard-hide`. Envoie `request-sync` à l'ouverture/rechargement et relaie un clic sur `btn-launch-from-display` en `postMessage({type: "start-round"})` vers le host.
- Dépendances : `qrcode` (vendor), aucune dépendance à `signalR`.
- Remarques : cette page est un pur "renderer" piloté par événements `message` — design cohérent (séparation stricte audio/scores sensibles réservés au host), mais toute la logique de rendu dupliquée avec `app.js` (~200 lignes en miroir) est le principal point de fragilité identifié dans ce module.

**`host/style.css`**
- Rôle : feuille de style principale, partagée par `index.html` et `display.html` — identité "affiche de concert/pochette vinyle" (fond sombre mat, encres franches, ombres dures décalées, typographie condensée), palette explicitement synchronisée avec `app/lib/theme.dart`.
- Contenu clé : variables CSS (`:root`) pour couleurs/typo/rayons ; styles génériques (boutons, inputs, pills de statut) ; composants réutilisés par les deux pages (`.game-code`, `.qr-container`, `.serie-intro-card`, `.avatar`/`.player-row`, `.player-list`/`.score-list`, `.timer-bar`/`.timer-fill` en "mètre segmenté" façon VU-mètre, `.cover-frame` avec vinyle tournant en pur CSS pour l'état "mystère", `.course-banner`, `.tag-picker`, `.score-chart-*`, `.overlay`).
- Remarques : bonne réutilisation via variables CSS et classes partagées entre les deux pages ; aucune remarque structurelle notable, contrairement au JS.

**`host/display.css`**
- Rôle : surcouche de `style.css` spécifique à l'écran public — grossit/épure l'affichage pour lecture à distance, sans jamais exposer plus d'information.
- Contenu clé : agrandissement de tailles de police/minuteur/QR/pochette, `.paused-banner` (bannière fixe), `.player-chips`/`.result-badges` (variantes "sans score"), `.qcm-options-display` (grille 2 colonnes).
- Remarques : commentaire en tête explicite ("aucune information sensible ne doit apparaître ici") — cohérent avec la séparation de responsabilités observée dans `display.js`.

**`host/vendor/qrcode.js`** — Librairie tierce (Kazuhiko Arase, licence MIT) de génération de QR codes, non modifiée. Exposée en global `qrcode`.
**`host/vendor/signalr.min.js`** — Client SignalR officiel Microsoft, minifié, vendoré.

### Vue d'ensemble host

`host/` implémente deux rôles distincts dans deux pages HTML séparées mais visuellement unifiées par un design system CSS partagé. **`index.html`/`app.js`** est le panneau de contrôle : seul point de connexion SignalR réel, seul lecteur audio (jamais envoyé aux joueurs), détenteur de tout l'état de partie côté client, et responsable de la configuration (thèmes, séries, paliers de mise) construite intégralement côté client puis soumise telle quelle au serveur. **`display.html`/`display.js`** est un pur écran de projection : fenêtre séparée ouverte par `window.open`, sans connexion réseau propre, alimentée uniquement par `postMessage` depuis le panneau de contrôle, avec un contenu volontairement filtré (jamais de scores en continu, jamais l'audio, jamais d'info avant le reveal). Communication bidirectionnelle minimale : le host pousse l'état, l'écran public ne renvoie que deux signaux (`"request-sync"` au chargement, `"start-round"` sur clic du bouton de lancement délégué).

Le couplage entre les deux pages est fort mais assumé : elles ne partagent aucun fichier JS/composant, seulement une **convention de nommage d'ID identique** et une **duplication délibérée** d'environ 200 lignes de logique de rendu — chaque copie porte un commentaire "miroir de..." pointant vers l'original, ce qui traduit une conscience du risque de désynchronisation sans y remédier structurellement.

Observation générale : l'ensemble est du JS/HTML/CSS vanilla sans aucune étape de build, ce qui explique en partie la duplication (pas de mécanisme d'import simple entre les deux fichiers sans bundler). `app.js` est le point le plus problématique pour une refonte : fichier unique de ~1600 lignes mélangeant transport réseau, state machine de 10 écrans, rendu DOM, lecture audio, minuteurs et logique métier de configuration de partie qui pourrait raisonnablement être déplacée côté serveur. Une refactorisation gagnerait à (1) extraire un module d'état partagé/observable plutôt que des variables globales éparses, (2) factoriser le rendu dupliqué entre `app.js` et `display.js` (même sans framework, un fichier JS commun chargé par les deux pages suffirait), et (3) séparer au minimum transport SignalR / logique de configuration / rendu DOM en fichiers distincts.

---

## 6. Inventaire fichier par fichier — Pipeline de données (`data/scripts/`, Python)

Le dossier `data/scripts/output/` contient uniquement des caches/résultats générés (JSON de cache API, CSV d'audit, `tag_editor.html` généré, listes `titres_*.txt`) — pas du code.

**`fetch_spotify_playlist.py`**
- Rôle : étape 1 du pipeline — récupère les morceaux d'une playlist Spotify publique (métadonnées + genres par artiste) et écrit un export JSON intermédiaire, jamais directement `tracks.json` (le schéma exige `filePath`, qui n'existe qu'après téléchargement audio).
- Contenu clé : auth Spotify "Client Credentials" (`get_access_token`) ; `extract_playlist_id` ; `_api_get` (GET générique, gestion 429/`Retry-After`) ; `fetch_playlist_tracks` (pagination, dédoublonnage) ; `fetch_artist_genres` (batch 50 artistes/requête) ; `nettoyer_titre` (regex retirant feat./remaster/remix/edit/mix/version/live, boucle jusqu'à stabilité) ; `build_track_entries`. CLI : `playlist`, `--output`, `--tags`.
- Dépendances : `requests`, `python-dotenv`. Écrit dans `output/<playlist_id>.json`. Réutilisé comme bibliothèque par `sync_daily.py`, `audit_genres.py`, `audit_reissue_years.py`, `audit_genres_deezer.py`, `audit_genres_musicbrainz.py`, `manual_redownload.py`.
- Remarques : fichier pivot du pipeline — presque tous les autres scripts qui touchent Spotify importent une fonction d'ici plutôt que de dupliquer l'appel API. Bonne discipline de réutilisation.

**`download_audio.py`**
- Rôle : étape 2 du pipeline — à partir d'un export JSON, télécharge l'audio YouTube (yt-dlp) + la cover Spotify, normalise le volume (ffmpeg loudnorm EBU R128), estime le point de départ du refrain (pychorus), et fusionne les entrées complètes dans `tracks.json`.
- Contenu clé : `rechercher_sur_youtube` (5 candidats via `ytsearch5`, pas seulement le premier résultat — retour utilisateur documenté : se limiter au 1er résultat flaguait à tort 13/20 morceaux) ; `_mots_significatifs`/`_titre_correspond` (filtre par recouvrement lexical, garde-fou contre un autre morceau du même artiste à durée proche — incident documenté) ; `meilleur_candidat` (pipeline en paliers : écarte live/suspects via `live_keywords.mots_suspects`, puis sans mot commun, puis proximité de durée) ; `telecharger_audio` ; `telecharger_cover` ; `normaliser_volume` (deux passes ffmpeg loudnorm) ; `estimer_refrain_start_ms` (pychorus) ; `main` (reprise après interruption, réécrit `tracks.json` après chaque morceau réussi, écarts de durée > tolérance → `output/a_verifier.json`, échecs → `output/echecs.json`). CLI : `export`, `--limit`, `--tolerance-seconds` (8s), `--ffmpeg-location`, `--delay-seconds` (3s).
- Dépendances : `yt_dlp`, `pychorus`, `requests`, `live_keywords.mots_suspects`. Lit/écrit `tracks.json` directement (le seul script "pipeline principal" à le faire en écriture incrémentale). Exporte plusieurs fonctions réutilisées telles quelles par `redownload_tracks.py` et `manual_redownload.py`.
- Remarques : script le plus complexe du dossier (349 lignes) et le seul qui écrit `tracks.json` en routine — logique de sélection multi-paliers dense mais bien commentée avec justification "retour utilisateur" à chaque étage.

**`sync_daily.py`**
- Rôle : orchestrateur pensé pour tourner quotidiennement via cron sur le Pi — lit `playlists.json` (liste de `{id, tags}`), fusionne toutes les playlists suivies, puis invoque `download_audio.py` sur l'export fusionné.
- Contenu clé : `charger_playlists` ; `fusionner_playlists` (fusion des tags par union d'ensembles) ; `main` lance `download_audio.py` en sous-processus. CLI : `--dry-run`, `--ffmpeg-location`, `--delay-seconds`.
- Dépendances : `playlists.json` (config, **actuellement vide**, `[]`) ; importe `fetch_spotify_playlist.py` ; invoque `download_audio.py` en sous-processus.
- Remarques : `playlists.json` vide suggère que ce script n'est pas encore réellement utilisé en cron sur le Pi (ou que le mécanisme de suivi de playlists a été abandonné au profit d'exports ponctuels lancés à la main) — point à clarifier avec l'utilisateur si la refonte doit conserver ou refondre ce mécanisme d'auto-sync.

**`live_keywords.py`**
- Rôle : module partagé (pas un script exécutable seul) — détecte les versions live/acoustiques/remix/cover à partir du titre d'une vidéo YouTube.
- Contenu clé : liste `MOTS_CLES_SUSPECTS`, `mots_suspects(titre_video, titre_officiel)` — retourne les mots-clés présents dans le titre vidéo mais absents du titre officiel.
- Dépendances : importé par `download_audio.py`, `audit_live_versions.py`, `redownload_tracks.py`.
- Remarques : petit module bien isolé, aucune duplication — modèle de factorisation propre entre 3 scripts.

**`audit_live_versions.py`**
- Rôle : audit a posteriori du catalogue déjà téléchargé — récupère (sans télécharger) le vrai titre YouTube de chaque morceau déjà dans `tracks.json` et exporte en CSV ceux dont le titre contient un mot-clé suspect absent du titre officiel. Ne modifie jamais `tracks.json`.
- Contenu clé : `recuperer_titre_youtube` (yt-dlp `skip_download`) ; cache `output/live_audit_cache.json` (persisté après chaque appel) ; `--refresh` ; export CSV (`utf-8-sig`, id/title/artist/mots_cles_suspects/youtube_title/youtube_url). CLI : `--limit`, `--delay-seconds` (1.5s), `--refresh`, `--out`.
- Dépendances : `live_keywords.mots_suspects`, `yt_dlp`. Lecture seule sur `tracks.json`. Sortie CSV alimente `redownload_tracks.py --csv`.
- Remarques : bon pendant "audit" du filtre appliqué en amont dans `download_audio.py`.

**`audit_genres.py`**
- Rôle : audit des tags "genre large" (9 buckets : metal/rock/pop/electro/rap/rnb-funk-jazz/variete-francaise/latino/monde) contre les genres Spotify frais de l'artiste.
- Contenu clé : dict `BUCKETS` (mots-clés par bucket, avec exceptions documentées) ; `suggerer_tags(genres)` ; pipeline en 3 étapes avec cache (`genre_audit_cache.json`). Préserve `DECADE_TAGS`/`TAGS_AD_HOC`. CLI : `--limit`, `--delay-seconds` (0.3s), `--refresh`, `--out`.
- Dépendances : `fetch_spotify_playlist._api_get`/`get_access_token`. Exporte `DECADE_TAGS`, `TAGS_AD_HOC`, `suggerer_tags`, `BUCKETS` — réimportés par `audit_genres_deezer.py`. Son cache est relu directement par `audit_genres_deezer.py`.
- Remarques : jamais réinjecté automatiquement — CSV toujours à relire manuellement au tableur avant `import_tags_csv.py`.

**`audit_genres_deezer.py`**
- Rôle : audit alternatif de `audit_genres.py` — recoupe chaque morceau vers Deezer via son ISRC (code unique par enregistrement) pour lire les genres de son album Deezer, au lieu des genres Spotify par artiste (ambigus en cas d'homonymie).
- Contenu clé : `DEEZER_BUCKETS` (25 genres top-niveau) ; `_deezer_get` ; pipeline en 5 étapes avec cache multi-clés, comparaison à 3 signaux (tags actuels / suggestion Spotify / suggestion Deezer) avec repérage des "corrections sûres". CLI : `--limit`, `--delay-seconds` (0.3s), `--refresh`, `--out`.
- Dépendances : importe `DECADE_TAGS`, `TAGS_AD_HOC`, `suggerer_tags` depuis `audit_genres.py` ; dépend fortement du cache déjà produit par `audit_genres.py` (échoue explicitement si absent).
- Remarques : bonne discipline de recoupement à deux sources indépendantes. Couplage de fichiers non trivial (dépendance dure au cache d'un autre script).

**`audit_genres_musicbrainz.py`**
- Rôle : **prototype** — troisième audit de genre, source MusicBrainz, recoupement par ISRC comme Deezer mais avec des tags "folksonomie" libres. Limité à un échantillon (défaut 150, contrainte 1 req/s MusicBrainz).
- Contenu clé : `MUSICBRAINZ_DELAY_SECONDS=1.0` (imposé) ; `interroger_musicbrainz` (endpoint `/isrc/{isrc}?inc=tags`) ; `BUCKETS` construit par inspection manuelle de vrais tags MusicBrainz ; `SEUIL_TAGS_BRUTS_MINIMUM=3` ; `construire_echantillon` (priorise les morceaux déjà signalés suspects par `audit_genres.py`, via un CSV daté en dur `genres_20260827.csv`). CLI : `--limit` (150), `--delay-seconds`, `--refresh`, `--out`.
- Dépendances : `fetch_spotify_playlist._api_get`/`get_access_token` ; lit en dur `output/genres_20260827.csv`.
- Remarques : chemin de fichier daté en dur — fragile si ce CSV est renommé/supprimé (dégrade proprement). Explicitement qualifié de "PROTOTYPE" dans sa docstring.

**`apply_genre_corrections.py`**
- Rôle : réinjecte dans `tracks.json` une relecture manuelle complète des genres, faite par l'utilisateur dans un fichier texte `output/genre_retravaillé.txt` (les suggestions automatiques jugées insuffisamment fiables).
- Contenu clé : format `Titre - Auteur | genre1, genre2 | années AAAA` ; `ARTIST_FIXES` ; `normaliser_cle` (aucun fuzzy matching, correspondance exacte après normalisation) ; `decade_tag` ; **remplace entièrement** le champ `tags` du morceau. CLI : `--dry-run`.
- Dépendances : `output/genre_retravaillé.txt` (généré manuellement, pas trouvé dans le listing `output/` actuel — probablement transitoire/déjà consommé). Écrit `tracks.json`.
- Remarques : seul script qui écrase les tags sans aucune comparaison de confiance — cohérent avec son statut de "vérité humaine finale". Probablement plus rejouable tel quel sans régénérer `genre_retravaillé.txt`.

**`audit_reissue_years.py`**
- Rôle : audit des morceaux dont `year` provient d'une réédition/remaster plutôt que de la sortie originale.
- Contenu clé : `rechercher_albums` (recherche Spotify `track:"..." artist:"..."`) ; retient l'année la plus ancienne parmi tous les résultats ; flague en CSV si l'écart dépasse un seuil (5 ans par défaut). Cache `reissue_audit_cache.json`. CLI : `--limit`, `--seuil-ecart-annees` (5), `--delay-seconds` (0.3s), `--refresh`, `--out`.
- Dépendances : `fetch_spotify_playlist._api_get`/`get_access_token`/`nettoyer_titre`. Lecture seule sur `tracks.json`. Sortie CSV réinjectée par `apply_reissue_years.py`.

**`apply_reissue_years.py`**
- Rôle : réinjecte les années corrigées identifiées par `audit_reissue_years.py` — remplace `year` et met à jour le tag décennie.
- Contenu clé : `tag_decennie(year)` ; remplace l'ancien tag décennie par le nouveau ; `--exclude ID1,ID2,...` pour ignorer des faux positifs. CLI : `csv_path`, `--dry-run`, `--exclude`.
- Dépendances : consomme la sortie CSV de `audit_reissue_years.py`. Écrit `tracks.json`.

**`export_tags_csv.py`**
- Rôle : export de `tracks.json` vers CSV pour relecture/correction des tags au tableur.
- Contenu clé : colonnes `id, title, artist, year, genres (lecture seule), tags (éditable)`. `--tag TAG` limite l'export à une seule catégorie. Encodage `utf-8-sig`. CLI : `--out`, `--tag`.
- Dépendances : lecture seule sur `tracks.json`. Sortie destinée à `import_tags_csv.py`.
- Remarques : script le plus simple et le plus stable du pipeline de curation.

**`import_tags_csv.py`**
- Rôle : réinjecte dans `tracks.json` les tags corrigés au tableur.
- Contenu clé : ne touche que le champ `tags` ; un id du CSV absent de `tracks.json` est signalé et ignoré ; un morceau absent du CSV garde ses tags inchangés (permet un import filtré par `--tag`) ; détecte les tags jamais vus dans le catalogue (fautes de frappe probables, sans jamais bloquer). CLI : `csv_path`, `--dry-run`.
- Dépendances : consomme la sortie de `export_tags_csv.py` (et, en pratique, tous les scripts d'audit de genre qui produisent la même colonne `tags`). Écrit `tracks.json`.
- Remarques : point d'écriture final commun à tous les audits de genre — bon point de convergence du pipeline.

**`build_tag_editor.py` + `tag_editor_template.html`**
- Rôle : génère un éditeur de tags autonome en page HTML locale (aucun serveur requis) à partir de `tracks.json`.
- Contenu clé (`build_tag_editor.py`) : `charger_hints_musicbrainz` (relit `output/genres_musicbrainz_20260828.csv` — chemin daté en dur — pour afficher les tags MusicBrainz bruts comme indice cliquable, jamais pré-appliqué) ; injecte les données dans le template via marqueurs `__DATA_JSON__`/`__HINTS_JSON__`/`__BUCKETS_JSON__`/`__TOTAL__`. CLI : `--out` (défaut `output/tag_editor.html`).
- Contenu clé (`tag_editor_template.html`, 402 lignes) : page HTML/CSS/JS autonome, thème sombre cohérent avec `host/style.css` — tableau avec recherche, cases à cocher pour les 9 buckets, tag libre, colonne indice MusicBrainz. Sauvegarde en `localStorage` (clé `blindify-tag-editor-v1`) au fur et à mesure — rien n'écrit `tracks.json` tant que l'export CSV n'est pas repassé dans `import_tags_csv.py`.
- Dépendances : `output/genres_musicbrainz_20260828.csv` (chemin daté en dur, dégrade proprement si absent). Sortie destinée à `import_tags_csv.py`.
- Remarques : bonne architecture "génère du HTML statique + état persistant côté navigateur, aucun backend". Chemin de cache MusicBrainz daté en dur partagé avec `audit_genres_musicbrainz.py` — une régénération sous un nom différent casserait silencieusement les deux à la fois.

**`manual_redownload.py`**
- Rôle : re-télécharge l'audio d'un morceau déjà présent dans `tracks.json` en imposant l'ID YouTube choisi à la main — complète `redownload_tracks.py` pour les cas où la recherche automatique échoue.
- Contenu clé : `--spotify-id` optionnel (met à jour spotifyId/album/durationMs/year/spotifyCoverUrl et retélécharge la cover) ; l'`id` du morceau n'est jamais modifié même si spotifyId change. Supprime l'ancien fichier audio avant retéléchargement. Documente un piège argparse (ID YouTube commençant par un tiret → nécessite `--`).
- Dépendances : réimporte plusieurs fonctions de `download_audio.py` et `fetch_spotify_playlist._api_get`/`get_access_token`. Écrit `tracks.json`.
- Remarques : bonne réutilisation de `download_audio.py` comme bibliothèque plutôt que duplication.

**`redownload_tracks.py`**
- Rôle : re-télécharge l'audio de morceaux déjà présents dans `tracks.json` via une nouvelle recherche automatique (contrairement à `manual_redownload.py`) — corrige un mauvais match repéré après coup.
- Contenu clé : réutilise `rechercher_sur_youtube`/`meilleur_candidat` de `download_audio.py` sur une requête neuve ; si le meilleur candidat reste suspect ou hors tolérance, le morceau est laissé tel quel et listé "à revoir manuellement" ; accepte des ids en argument positionnel ou via `--csv` (typiquement la sortie de `audit_live_versions.py`).
- Dépendances : réimporte `download_audio.py` et `live_keywords.mots_suspects`. Écrit `tracks.json`.
- Remarques : bon exemple de boucle de rétroaction complète : `audit_live_versions.py` détecte → `redownload_tracks.py --csv` corrige automatiquement ce qui peut l'être → les cas restants passent à `manual_redownload.py`.

**`data/scripts/output/`** (non détaillé fichier par fichier) — Caches API indexés par id/ISRC, CSV d'audit horodatés destinés à relecture tableur, exports intermédiaires de playlists, rapports d'échec/vérification, listes de curation manuelle, fichiers de nettoyage ponctuels, l'éditeur HTML généré, une visualisation. Aucun de ces fichiers n'est versionné comme source de vérité — tous régénérables à partir de `tracks.json` et des API externes.

### Vue d'ensemble pipeline data

**Flow complet d'ajout d'un morceau** : `fetch_spotify_playlist.py <playlist>` (ou `sync_daily.py` pour plusieurs playlists suivies via `playlists.json`, actuellement vide) récupère titre/artiste/album/genres/durée/cover-URL depuis Spotify et écrit un export JSON intermédiaire → `download_audio.py <export>` cherche chaque morceau sur YouTube (5 candidats, filtre live/mots suspects, filtre recouvrement lexical, tolérance de durée ±8s), télécharge l'audio + la cover, normalise le volume, estime `refrainStartMs`, et fusionne l'entrée complète dans `tracks.json` → en aval, une boucle de curation manuelle par tableur (`export_tags_csv.py` → édition Excel → `import_tags_csv.py`) ou par éditeur HTML autonome (`build_tag_editor.py`) affine les tags thématiques, assistée par trois audits de genre indépendants et convergents (Spotify/`audit_genres.py`, Deezer/`audit_genres_deezer.py` par ISRC, MusicBrainz/`audit_genres_musicbrainz.py` par ISRC en prototype/échantillon) et un pipeline symétrique pour les années de réédition (`audit_reissue_years.py` → `apply_reissue_years.py`) et les versions live mal téléchargées (`audit_live_versions.py` → `redownload_tracks.py`/`manual_redownload.py`).

**Scripts "pipeline principal" (réutilisables, cœur du flux)** : `fetch_spotify_playlist.py`, `download_audio.py`, `sync_daily.py` (orchestrateur cron, mais actuellement non fonctionnel faute de playlists configurées), `export_tags_csv.py`/`import_tags_csv.py` (boucle de curation stable et récurrente).

**Scripts "ponctuels" (audit / one-shot / correction ciblée)** : `audit_genres.py`, `audit_genres_deezer.py`, `audit_genres_musicbrainz.py` (prototype explicite), `audit_reissue_years.py`/`apply_reissue_years.py`, `audit_live_versions.py`, `apply_genre_corrections.py` (dépend d'un fichier déjà consommé, non rejouable en l'état), `manual_redownload.py`, `redownload_tracks.py`, `build_tag_editor.py`.

**Observations générales** :
- **Redondance assumée sur l'audit de genre** : trois scripts (Spotify-artiste, Deezer-ISRC, MusicBrainz-ISRC prototype) attaquent le même problème avec des sources différentes, chacun maintenant son propre dictionnaire de mots-clés par bucket avec des nuances empiriques précises (des dizaines de cas réels documentés en commentaire). Ce n'est pas une duplication accidentelle mais une stratégie délibérée de triangulation — l'aboutissement en pratique (`apply_genre_corrections.py` + `build_tag_editor.py`) montre cependant que l'utilisateur a fini par préférer un encodage largement manuel, ce qui questionne la valeur d'entretien continu des 3 scripts d'audit automatique dans une refonte future.
- **Absence totale de tests automatisés** sur les 17 scripts — contraste marqué avec la suite xUnit conséquente du backend .NET. La logique la plus critique (nettoyage de titre par regex, sélection du meilleur candidat YouTube, filtrage des tags MusicBrainz par seuil de confiance) n'est validée que par lecture manuelle et retours d'usage réel documentés en commentaire.
- **Gestion des erreurs cohérente et pragmatique** : quasiment tous les scripts qui font des appels réseau en boucle capturent les exceptions par morceau pour ne jamais interrompre un lot entier sur un seul échec, avec caches persistés au fur et à mesure — bon réflexe de résilience pour des scripts longue durée sur des API externes.
- **Chemins de fichiers datés en dur** : `audit_genres_musicbrainz.py` (`genres_20260827.csv`) et `build_tag_editor.py` (`genres_musicbrainz_20260828.csv`) référencent des noms de fichiers figés à une date précise plutôt qu'un "dernier fichier disponible" calculé dynamiquement — dégradent proprement si absents, mais une régénération sous un nom différent romprait silencieusement le lien sans qu'aucun test ne le détecte.
- **Bon niveau de documentation "pourquoi"** : chaque script démarre par une docstring qui explique le retour utilisateur ou l'incident concret ayant motivé son existence (daté), avec des exemples réels — traçabilité largement supérieure à la moyenne des scripts d'outillage data, facilitera beaucoup une éventuelle refonte car l'intention métier est déjà écrite noir sur blanc plutôt qu'à reconstituer depuis l'historique git.
- **Aucune commande git exécutée par ces scripts** — toute la responsabilité de commit/versionnement de `tracks.json` après une session de curation reste manuelle, cohérent avec CLAUDE.md.

---

## 7. Synthèse des points de friction identifiés (candidats de refactor)

Aucun de ces points ne reflète un problème de correction fonctionnelle (le code est globalement propre, testé côté backend, et très bien documenté avec l'historique des retours utilisateur) — ce sont des candidats de **simplification/factorisation**, à prioriser selon ce que la refonte souhaite cibler en premier.

1. **`host/app.js` (1614 lignes)** — mélange rendu DOM, réseau SignalR, audio, configuration de partie et logique d'enchaînement automatique dans un seul fichier sans modules. Le plus gros fichier du repo, et probablement la source principale du retour utilisateur "ergonomie pas folle" (voir `niceToHave.md`).
2. **Duplication `round_screen.dart` / `bonus_question_screen.dart`** (Flutter) — deux écrans de ~320 lignes quasi identiques (ticker, auto-submit, 3 sous-widgets de réponse), jamais factorisés ; `bonus_stake_screen.dart` a aussi sa propre copie du widget `_Banner` (3 copies au total).
3. **Duplication `app.js` / `display.js`** (host) — couleurs d'avatar, transitions d'écran, graphique de scores, réimplémentés indépendamment des deux côtés faute de mécanisme de partage de module en JS vanilla.
4. **Palette de couleurs dupliquée 3 fois** — `app/lib/theme.dart` (Dart), `host/style.css` (CSS), `host/app.js`/`display.js` (JS) — aucune source unique, toute évolution visuelle doit être répliquée à la main partout.
5. **4 primitives d'enchaînement automatique quasi jumelles** dans `app.js` (`scheduleAutoNext`, `scheduleAutoNextSerie`, `scheduleAutoEndGame`, `scheduleAutoStartSerie`) — bon candidat à une primitive unique "minuteur d'enchaînement annulable".
6. **`GameHub.cs` (531 lignes)** — concentre l'orchestration de tous les services applicatifs ET deux fonctions de logique métier de présentation (feintes QCM) qui pourraient migrer vers `Blindify.Application`, plus la construction de DTOs qui pourrait suivre le modèle de `ScoreDtoBuilder`.
7. **Deux `RoundTimerCoordinator`/`BonusTimerCoordinator` quasi jumeaux** — même squelette de polling à 250ms (dictionnaire de CTS, démarrage/annulation, boucle), dupliqué plutôt qu'unifié derrière une primitive générique.
8. **5 scripts Python obsolètes/superflus** (3 audits de genres + éditeur visuel + template) — la solution retenue in fine (`apply_genre_corrections.py`, dernier commit du repo) les a rendus caducs sans qu'ils soient nettoyés du repo ; `sync_daily.py` non fonctionnel en l'état (`playlists.json` vide).
9. **Getters de présentation dupliqués** (`RoundEnded.reponseAttendue` / `BonusResult.reponseAttendue` côté Flutter) — même logique, deux implémentations légèrement différentes.
10. **Pas de verrouillage explicite sur `GameSession`** (backend) malgré des mutations concurrentes possibles (plusieurs connexions SignalR + timers de fond simultanés) — fonctionne en pratique (faible charge, réseau local familial) mais fragile si le modèle de concurrence devait changer.
