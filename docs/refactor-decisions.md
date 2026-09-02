# Blindify — Décisions de refonte

> Document de décisions issu de la discussion du 2026-08-29, sur la base de l'audit `refactor-context.md`.
> Il fixe **ce qui est décidé et pourquoi**, ainsi que ce qui a été **explicitement écarté** — pour ne pas
> avoir à re-arbitrer les mêmes points dans six mois.

---

## 0. Cadre général

**Objectif déclaré** : faciliter la maintenance future. Pas de douleur fonctionnelle à résoudre, le code
actuel marche.

**Critère retenu pour découper un fichier** : le nombre de *raisons de le rouvrir*, pas le nombre de lignes.
On découpe quand il y a plusieurs responsabilités mélangées ou une duplication réelle. Jamais sur la taille
seule.

> Conséquence directe : `QcmGenerator.cs` (247 l.) ne sera **pas** découpé — ses règles se lisent en séquence
> (piège → ancre unique → repli année → repli global → filet de sécurité) et les séparer nuirait à la
> compréhension. Idem pour les gros fichiers de tests.

**Règle de discipline** : on ne touche pas à du code couvert par des tests qui passent, sauf si le refactor
supprime une duplication réelle ou déplace une responsabilité mal placée.

> Conséquence : `QcmGenerator`, `RoundService`, `AnswerMatcher`, `FilmNameResolver`, `BonusRoundService`
> restent en l'état. Les fichiers les plus denses du repo sont aussi les mieux protégés.

**Périmètre accepté** : tout le repo (backend, Flutter, host, scripts data).

---

## 1. Remonter la logique de configuration côté serveur

**Décidé.** C'est le seul changement de la liste qui améliore l'architecture plutôt que la lisibilité.

Aujourd'hui `host/app.js` calcule la configuration de partie et le serveur la rejoue telle quelle sans
rien vérifier :

- `assignerThemesAuxSeries` — répartition des thèmes par série, sans répétition tant que le vivier n'est
  pas épuisé
- `paliersPourSerie` — calcul géométrique des paliers de mise bonus (convergence vers 3000 pts à la
  dernière série)
- `pickRandomRoundModes`

**Cible** : ces fonctions migrent dans `Blindify.Application`. Le client envoie une **intention**
(nombre de séries, nombre de rounds, vivier de thèmes, durées) et non plus un résultat calculé.
`ConfigurerPartieRequestDto` se simplifie en conséquence.

**Gains** :
1. `host/app.js` maigrit d'environ 250 lignes (`config.js` passe de ~250 à ~60 lignes)
2. La logique devient testable en xUnit comme le reste du backend
3. Une classe entière de désynchronisation client/serveur disparaît

---

## 2. Découpage de `host/` en modules ES natifs

**Décidé.** C'est le chantier principal.

### Stack

Modules ES natifs (`<script type="module">`), **sans bundler ni framework**.

*Raison* : le backend sert déjà `host/` en statique via `Host:StaticPath`, donc les modules fonctionnent
sans rien changer au déploiement ni au Dockerfile. Un Vite + Svelte apporterait en plus la suppression du
couplage DOM-par-ID et la fin de la duplication des deux fichiers HTML, mais au prix d'une étape de build
dans une stack dont la contrainte explicite est « servable en statique sans compilation », sur un Pi.
Les modules ES encaissent environ 80 % du bénéfice pour un coût très inférieur.

**À rediscuter plus tard uniquement si** les deux fichiers HTML restent le point de friction une fois le
découpage fait.

**Contrainte induite** : `display.html` ne peut plus être ouvert en `file://` (les modules ES sont bloqués
par CORS). Le `window.open` depuis la page servie par le backend règle ça — c'est déjà le fonctionnement
actuel.

### Règle de dépendance

> **Le réseau écrit dans l'état, l'état écrit dans le DOM, et jamais l'inverse.**
> Un module ne connaît que la couche en dessous de lui.

C'est le cœur du refactor. Aujourd'hui un handler SignalR mute une globale, écrit dans le DOM et appelle
`syncDisplay()` dans la foulée — c'est ce mélange qui rend le fichier illisible, pas sa longueur.

### Mode de rendu : réactif

**Décidé : rendu réactif.** `state.js` notifie ses abonnés, `render.js` redessine l'écran actif. Un seul
chemin entre « l'état a changé » et « l'écran est à jour ».

*Raison* : c'est ce qui rend un ajout de feature indolore — on pose le champ dans l'état, on l'affiche dans
le rendu de l'écran, terminé. Abordable ici parce que le host est déjà une machine à états à 10 écrans :
la notion d'« écran actif » existe, il ne manque que la boucle.

*Alternative écartée* : le rendu impératif (handlers qui appellent explicitement les fonctions de rendu).
Migration plus sûre, mais conserve le défaut de fond — chaque nouvelle donnée affichée oblige à se rappeler
quels endroits du DOM la reflètent.

**Deux précautions non négociables** :
1. La balise `<audio>` reste **hors du flux de rendu**, pilotée uniquement par `audio.js`. Elle ne doit
   jamais être re-render.
2. Les `<input>` de configuration sont exclus du re-render tant que l'utilisateur tape.

### Arborescence cible

**Couche partagée** — importée à l'identique par `app.js` et `display.js`. C'est elle qui supprime les
~200 lignes actuellement dupliquées en miroir.

| Fichier | Contenu |
|---|---|
| `shared/palette.js` | 12 couleurs d'avatar, `COULEURS_PODIUM`, `couleurAvatar`, `couleurClassement` |
| `shared/format.js` | `lettreSerie`, `libelleTheme`, `libelleReveal` |
| `shared/components.js` | `avatarHtml`, `renderScoreChart`, `renderJoinQrCode`, listes joueurs/scores |

**Couche host** :

| Fichier | Responsabilité |
|---|---|
| `state.js` | Objet d'état unique (remplace les ~25 globales) + `subscribe(callback)` |
| `transport.js` | `HubConnection`, `tenterConnexion`, invocations sortantes. **Ne touche jamais au DOM.** |
| `handlers.js` | Un handler par événement du contrat. Mute l'état, rien d'autre. Seul fichier qui connaît le vocabulaire serveur. |
| `render.js` | État → DOM. À découper par écran s'il grossit. |
| `audio.js` | `playAudio`, `jouerRefrain`, `fadeAudioVolume`, gestion du blocage autoplay |
| `timers.js` | Minuteur visuel + primitive d'enchaînement annulable (voir §3) |
| `display-bridge.js` | `syncDisplay` et le `postMessage` vers l'écran public |
| `config.js` | Collecte des choix dans l'UI + envoi de l'intention au serveur (voir §1) |
| `main.js` | Bootstrap et câblage |

`display.js` adopte exactement le même schéma : `appliquerEtat` écrit dans son propre `state`, le rendu suit.

### Ordre d'exécution

1. **`shared/` d'abord** — bénéfice immédiat, risque nul, et ça valide que les modules ES fonctionnent
   correctement servis par le backend.
2. **`state.js` + `handlers.js` + `render.js` ensemble** — ce trio ne se découpe pas en étapes
   intermédiaires sans se retrouver dans un état bâtard.
3. `audio.js`, `timers.js`, `display-bridge.js` — se détachent ensuite trivialement.

---

## 3. Primitive d'enchaînement annulable

**Décidé**, malgré l'absence de bug constaté.

Les quatre `scheduleAutoNext`, `scheduleAutoNextSerie`, `scheduleAutoEndGame`, `scheduleAutoStartSerie`
fusionnent en une primitive unique `minuteurAnnulable(delaiMs, action)` avec un registre de timers
annulables, dans `timers.js`.

*Raison retenue* : ce n'est pas de la prévention contre un bug hypothétique, c'est une réduction de charge
mentale. Quatre `setTimeout` parallèles à annuler dans le bon ordre, c'est du code à se remémorer
intégralement à chaque évolution du flow de jeu.

*Coût estimé* : ~1 h.

---

## 4. Flutter — écran de réponse générique + tests ciblés

### Factorisation

**Décidé.** `round_screen.dart` (316 l.) et `bonus_question_screen.dart` (319 l.) fusionnent en un seul
écran « phase de réponse » générique, paramétré par :

- la source de données (round classique / question bonus)
- la cible (`RoundCible`)
- le callback de soumission
- la présence du mode course (optionnel)

Cela supprime ~640 lignes dupliquées, les 3 sous-widgets de réponse en double
(`_QcmAnswers`, `_LetterAnswer`, `_TextAnswer`) et les **3 copies de `_Banner`**
(`round_screen`, `bonus_question_screen`, `bonus_stake_screen`).

À traiter dans la foulée, même nature : le getter `reponseAttendue` dupliqué entre `RoundEnded` et
`BonusResult` (deux implémentations légèrement différentes du même besoin de présentation).

### Tests

**Décidé** : des tests **unitaires purs sur `GameConnection`**, pas des tests de widget.

*Raison* : la valeur est dans la logique aujourd'hui couverte par rien, et précisément aux endroits qui ont
déjà généré des retours utilisateur. Ce sont des tests sur un `ChangeNotifier`, sans `WidgetTester` — rapides
à écrire.

Cibles prioritaires :
- La garde anti-race du mode course (`BonusResult` arrivant avant la fin du timer d'intro de 2500 ms)
- La double consommation de `pendingJoinCode` (`initState` + `build`)
- Les gardes anti-double-soumission (`roundAnswered` / `bonusStakeEnvoyee` / `bonusAnswered`)

Les 3 sous-widgets de réponse, une fois factorisés, se valident visuellement en jouant une partie.

### Écarté

**Le découpage de `GameConnection`** (495 l., God Object assumé et documenté). Il fonctionne, et le découper
sans tests préexistants serait un pari. Les tests ci-dessus le rendront éventuellement possible plus tard,
mais ce n'est pas un objectif.

---

## 5. Verrou sur `GameSession`

**Décidé.** Un `lock` par session dans `GameSessionStore`, pris dans `GameHub` et les deux
`TimerCoordinator`.

*Raison* : c'est une vraie race, pas une question d'élégance. Plusieurs connexions SignalR et des timers de
fond peuvent muter `session.Players` / `round.Reponses` en parallèle sans synchronisation. Faible
probabilité d'impact au volume actuel (réseau local familial), mais le coût de correction est dérisoire.

*Coût estimé* : ~1 h.

---

## 6. Scripts data — Option A : archive

**Décidé : option A.** Déplacer les scripts caducs dans `data/scripts/attic/`, avec un README d'une ligne
par script expliquant pourquoi il est là.

**Scripts concernés** (rendus caducs par `apply_genre_corrections.py`, qui a acté le choix d'un encodage
largement manuel) :
- `audit_genres.py`
- `audit_genres_deezer.py`
- `audit_genres_musicbrainz.py` (prototype explicite)
- `build_tag_editor.py` + `tag_editor_template.html`

*Raison de l'archive plutôt que la suppression* : on conserve le travail empirique accumulé — les
dictionnaires de mots-clés par bucket documentent des dizaines de cas réels en commentaire. Suffisamment
coûteux à reconstituer pour ne pas le jeter, suffisamment mort pour ne pas le maintenir.

**Attention à la dépendance croisée** : `audit_genres_deezer.py` importe `DECADE_TAGS`, `TAGS_AD_HOC` et
`suggerer_tags` depuis `audit_genres.py`, et dépend en dur de son cache. Les déplacer **ensemble** dans
`attic/`, sinon les imports cassent.

*Option écartée* : la consolidation des 3 audits en un script multi-source (`--source spotify|deezer|musicbrainz`).
Séduisant sur le papier — structure commune, seuls les dictionnaires de buckets diffèrent — mais c'est du
travail sur du code dont l'usage a déjà été abandonné.

### À traiter en parallèle

**Chemins datés en dur** — à remplacer par « dernier fichier correspondant au motif » sur ce qui reste actif :
- `audit_genres_musicbrainz.py` → `output/genres_20260827.csv`
- `build_tag_editor.py` → `output/genres_musicbrainz_20260828.csv`

Ces deux-là partent à l'attic, donc le point devient sans objet — **sauf** si l'un d'eux est finalement
conservé. À vérifier au moment du déplacement.

### En suspens

**`sync_daily.py`** — non tranché. `playlists.json` est actuellement vide (`[]`), donc l'orchestrateur cron
n'est pas en service. Question ouverte : le flux réel d'ajout de morceaux est-il un lancement manuel de
`fetch_spotify_playlist.py` quand une playlist est à intégrer, ou l'auto-synchronisation par le Pi est-elle
un objectif réel ? La réponse décide entre réparation et archivage.

### Non concernés (pipeline actif, on n'y touche pas)

`fetch_spotify_playlist.py`, `download_audio.py`, `live_keywords.py`, `export_tags_csv.py`,
`import_tags_csv.py`, `audit_live_versions.py`, `redownload_tracks.py`, `manual_redownload.py`,
`audit_reissue_years.py`, `apply_reissue_years.py`, `apply_genre_corrections.py`.

---

## 7. Contrat SignalR — test de non-régression

**Décidé : pas de génération de code.**

Une chaîne C# → Dart/JS exigerait un schéma intermédiaire, un générateur à maintenir et une étape de build
dans un projet qui n'en a pas — pour un contrat qui bouge quelques fois par an.

**À la place** : un test xUnit qui sérialise chaque DTO du dossier `Contracts/` et le compare à un JSON de
référence versionné. Quand un champ est renommé, le test casse et indique exactement quel modèle Dart et
quel handler JS mettre à jour.

Cela ne supprime pas la triple copie du contrat (DTOs C#, modèles Dart, objets ad hoc JS), mais supprime la
**désynchronisation silencieuse**, qui est le seul vrai problème.

### Palette de couleurs (3 exemplaires)

`app/lib/theme.dart`, `host/style.css`, `shared/palette.js`.

**Piste, non tranchée** : un `design-tokens.json` unique lu par un petit script qui régénère les constantes
dans les trois langages. Plus simple qu'un test dans ce cas précis. Marginal — à faire seulement si l'envie
s'en fait sentir.

---

## 8. Récapitulatif

| # | Décision | Statut |
|---|---|---|
| 1 | Logique de config (`assignerThemesAuxSeries`, `paliersPourSerie`, `pickRandomRoundModes`) → `Blindify.Application` | Décidé |
| 2 | `host/` découpé en modules ES natifs, rendu **réactif**, couche `shared/` commune aux deux pages | Décidé |
| 3 | Primitive `minuteurAnnulable` unique remplaçant les 4 `scheduleAuto*` | Décidé |
| 4 | Écran de réponse Flutter générique + tests unitaires sur `GameConnection` | Décidé |
| 5 | `lock` par session sur `GameSession` | Décidé |
| 6 | Scripts de genre caducs → `data/scripts/attic/` | Décidé |
| 7 | Test xUnit de non-régression du contrat SignalR | Décidé |
| — | Découpage de `GameConnection` (Flutter) | **Écarté** |
| — | Découpage de `QcmGenerator` et autres fichiers denses testés | **Écarté** |
| — | Bundler / framework côté host | **Écarté** (rediscutable après §2) |
| — | Consolidation des 3 audits de genre en script multi-source | **Écarté** |
| — | Génération de code pour le contrat SignalR | **Écarté** |
| — | Sort de `sync_daily.py` | **En suspens** |
| — | `design-tokens.json` pour la palette | **En suspens** |

### Ordre d'attaque suggéré

1. §6 archive des scripts (dix minutes, dégage le terrain)
2. §5 lock + §3 primitive de minuteur (~2 h, indépendants du reste)
3. §2 étape 1 : couche `shared/` (valide les modules ES, supprime les 200 lignes en miroir)
4. §1 remontée de la config côté serveur (allège `config.js` avant de le réécrire)
5. §2 étape 2 : `state.js` + `handlers.js` + `render.js` — le gros morceau
6. §2 étape 3 : `audio.js`, `timers.js`, `display-bridge.js`
7. §4 Flutter
8. §7 test de contrat
