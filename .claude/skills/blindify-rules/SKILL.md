---
name: blindify-rules
description: Règles du jeu et contrat technique du projet Blindify (blindtest local multijoueur). Utiliser CE SKILL dès qu'une tâche touche au backend SignalR, au cycle de vie des rounds, au scoring, à la question bonus, au mode équipes, ou au schéma tracks.json — même si l'utilisateur ne mentionne pas explicitement "Blindify" ou "règles du jeu". Toute implémentation ou modification de la logique de jeu doit consulter ce skill avant d'écrire du code.
---

# Règles du jeu Blindify

Résumé opérationnel. Le détail complet (schémas, diagrammes, exemples JSON, contrat SignalR intégral) est dans **`docs/architecture.md`** à la racine du repo — le lire si une info précise manque ici.

## Cycle d'un round classique

1. Lecture audio → horodatage serveur `débutRound`.
2. Chaque joueur répond **indépendamment**, à son rythme (pas de buzzer exclusif, pas de verrouillage). Un seul essai par joueur et par round.
3. Points calculés à la réception de la réponse :
   ```
   tempsÉcoulé = (maintenant - débutRound) - duréeEnPauseMs
   pointsEnJeu = max(min, max - (tempsÉcoulé / duréeFenêtre) × (max - min))
   ```
   Juste → `+pointsEnJeu`. Faux → `-pointsEnJeu × 0.5` (pénalité réduite pour inciter à toujours tenter une réponse plutôt qu'à s'abstenir — voir `docs/architecture.md` section 6 pour la justification). Pas de réponse en fin de round → `-2` fixe (V2, était `-5` ; `ConfigurerPartie` rejette une valeur trop sévère par rapport au ratio de pénalité et à `PointsMin`).
4. `duréeEnPauseMs` neutralise le temps où la partie était en pause (voir plus bas).

## Cible du round (Titre / Auteur / Film / Année)

Tirage pondéré (`GameConfig.PoidsCibleTitre`/`PoidsCibleAuteur`/`PoidsCibleAnnee`, 40/40/20 par défaut, V2) au démarrage du round — sauf morceaux tagués `"disney"` dans `tracks.json`, où la cible est **toujours forcée à `Film`** : ni le titre réel de la chanson ni l'artiste crédité (souvent la voix/l'acteur, imprévisible) ne sont des questions jouables pour ce type de contenu. `Film` compare la réponse au nom du film déduit de `Track.Album` (nettoyé des suffixes de bande originale — voir `FilmNameResolver`), pas au titre de la chanson. `Année` (V2) n'est éligible que si `Track.Year` est connu ; en mode saisie le score est dégressif par proximité (tolérance `SeriesConfig.ToleranceAnnee`) plutôt que juste/faux — voir `docs/architecture.md` section 6. Jamais combinée à Mode PremiereLettre (bascule alors vers TapeReponse).

## QCM

3 distracteurs par défaut, tirés du même pool genre/tag, **en excluant tout artiste déjà choisi** (correct ou distracteur précédent) — deux options créditées au même artiste sont illisibles en cible Auteur. Si le morceau a des `trapWith` définis dans `tracks.json`, ~15-20 % de chance d'utiliser un piège à la place — ne doit jamais devenir systématique (probabilité configurable).

Si le pool genre/tag ne contient pas assez de morceaux pour compléter les 3 distracteurs (thème trop niche, ou morceau sans genre renseigné), repli sur la même décennie (`Track.Year`) avant de retomber sur le pool global (`tracks.json` entier) totalement aléatoire. Toujours 4 options, jamais de round bloqué — au pire, un même artiste peut réapparaître plutôt que de bloquer le round.

## Question bonus (fin de série)

Deux phases, dans cet ordre strict :
1. **Mise à l'aveugle** : le joueur choisit un palier (4 options, croissants par série via un ratio géométrique constant `GameConfig.FacteurProgressionPaliers`, 1.6 par défaut, V2) **avant** de connaître la question. Pas de choix dans le délai → palier "safe" par défaut.
2. **Question** : morceau révélé, timer fixe **sans dégressivité**, audio ralenti par défaut (`playbackRate` réduit, paramètre désactivable). Un seul essai. Pas de réponse → traité comme faux (perte de la mise).

Après la question bonus de chaque série : le tableau général **peut** s'afficher (au moins une fois par partie, pas forcément à chaque série — par défaut vers la série médiane, ou déclenché manuellement par le host).

## Mode équipes (optionnel)

Si `modeÉquipe` actif : chaque joueur répond toujours individuellement, mais les points vont au score de son équipe (`teamId`), pas à un score individuel.

## Titres de fin de partie (V2)

`GameHub.EndGame()` calcule via `TitresService.CalculerTitres` (statique, `Blindify.Application/Awards/`) 11 titres possibles (ÉCLAIR, SNIPER, SPÉCIALISTE {tag}, HORLOGE SUISSE, ROI DU BONUS, KAMIKAZE, TORTUE PRUDENTE, REMONTADA, TOMBÉ DANS LE PANNEAU, CHAT NOIR, et FIDÈLE en repli pour qui n'a rien) — seuils et règle d'attribution (rareté croissante, plafond 2/joueur, égalités partagées) détaillés dans `docs/architecture.md` section "Titres de fin de partie". Toujours individuels, même en mode équipe. Diffusés dans `GameEnded` via `titres: TitreDto[]`, même DTO host/joueurs (pas de secret).

## Signalement en direct (V2)

`GameHub.SignalerMorceau({trackId, raison, commentaire?})` — host **ou** admin authentifié uniquement, jamais les joueurs, depuis la liste « Morceaux joués dans cette partie » (construite côté client à partir de `RoundEnded`/`BonusResult`, jamais avant le reveal — l'admin est aussi un joueur). Vérifie que le morceau a bien été joué dans la session, déduplique même morceau + même raison + même partie, écrit dans `data/flags.json` (`Blindify.Infrastructure.Flags.FlagsRepository`, atomique). 7 raisons (`RaisonSignalement`), 4 bloquantes (`PasSaPlace`, `MauvaiseVersion`, `AudioDefectueux`, `MetadonneesFausses`) qui excluent le morceau de `SelectionnerMorceaux` tant qu'il n'apparaît pas dans `data/flags_resolutions.json` (lecture seule, pipeline `data/scripts/`) — réglable par `GameConfig.ExclureMorceauxSignales`. Diffuse `MorceauSignale` au host et aux admins uniquement, jamais aux joueurs.

## Reconnexion joueur et host

- Un `Player` est identifié par un `playerId` **stable**, généré et persisté côté client Flutter — jamais par le `connectionId` SignalR, qui change à chaque reconnexion (coupure WiFi, verrouillage d'écran). `JoinGame(code, nom, playerId)` sert aussi bien à rejoindre qu'à se reconnecter : si le `playerId` existe déjà dans la partie, le serveur réassocie le nouveau `connectionId` et renvoie l'état existant du joueur (score, équipe) plutôt que d'en créer un nouveau.
- **V2** : la reconnexion se fait aussi automatiquement, sans rappeler `JoinGame` — le client ouvre le hub avec `?code&playerId` dans l'URL, `GameHub.OnConnectedAsync` réassocie et renvoie un événement `EtatCourant`. `JoinGame` reste nécessaire pour le tout premier join et sert de filet de sécurité. Un délai de grâce (`GameConfig.DelaiGraceDeconnexionMs`, 5 s par défaut) retarde `PlayerDisconnected` pour ne pas signaler une micro-coupure suivie d'une reconnexion rapide.
- **V2** : `SubmitAnswer`/`SelectStake`/`SubmitBonusAnswer` portent un `roundId` (repris de l'événement `RoundStarted`/`BonusStakeOptions`/`BonusQuestionStarted`) — une soumission dont le `roundId` ne correspond plus au round courant est ignorée silencieusement (réponse en retard après une reconnexion, round suivant déjà démarré).
- Le host (page web PC) peut resynchroniser son état après un refresh/crash via `RejoinAsHost(code, hostSecret)`, qui renvoie l'état courant (morceau, mode, position audio théorique, `enPause`) pour reprendre la lecture sans redémarrer le morceau. `hostSecret` (renvoyé par `CreateGame`, distinct du code public) empêche un joueur connaissant seulement le code de voler le contrôle host.

## Pause

Le host peut geler la partie à tout moment (round classique ou bonus). Reprise de l'audio exactement là où il s'était arrêté. Toute soumission reçue pendant `enPause = true` est rejetée côté serveur (ne pas faire confiance uniquement à l'UI client).

## Contraintes non négociables

- Pas de base de données — `tracks.json` chargé en mémoire, source de vérité unique pour les métadonnées.
- `tracks.json` (métadonnées éditoriales) et `stats.json` (compteurs runtime type `playCount`) sont deux fichiers séparés — le backend n'écrit jamais dans `tracks.json`, et le script d'import CSV ne touche jamais à `stats.json`. Même principe pour `flags.json` (backend-writable) / `flags_resolutions.json` (écrit uniquement par le pipeline `data/scripts/`, V2).
- Aucun paramètre de timing/scoring/mise en dur : tout passe par une config par partie/série.
- L'audio ne sort jamais vers les clients joueurs (Flutter) — seul le client host le joue.
- Seul le backend est dockerisé ; jamais le frontend.
- Identification des joueurs par `playerId` stable côté client, jamais par `connectionId` SignalR.

## Quand aller lire `docs/architecture.md`

- Schéma complet de `tracks.json` (champs `genres`, `tags`, `trapWith`, `coverPath`, etc.)
- Contrat SignalR intégral (toutes les méthodes host/joueur et tous les événements serveur)
- Table des paramètres de configuration et leurs valeurs par défaut recommandées
- Détails du déploiement Docker (volumes, docker-compose)
