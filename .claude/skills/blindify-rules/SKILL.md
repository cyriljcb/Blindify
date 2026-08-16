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
   Juste → `+pointsEnJeu`. Faux → `-pointsEnJeu × 0.5` (pénalité réduite pour inciter à toujours tenter une réponse plutôt qu'à s'abstenir — voir `docs/architecture.md` section 6 pour la justification). Pas de réponse en fin de round → `-5` fixe.
4. `duréeEnPauseMs` neutralise le temps où la partie était en pause (voir plus bas).

## QCM

3 distracteurs par défaut, tirés du même pool genre/tag. Si le morceau a des `trapWith` définis dans `tracks.json`, ~15-20 % de chance d'utiliser un piège à la place — ne doit jamais devenir systématique (probabilité configurable).

Si le pool genre/tag ne contient pas assez de morceaux pour compléter les 3 distracteurs (thème trop niche), compléter avec le pool global (`tracks.json` entier). Toujours 4 options, jamais de round bloqué.

## Question bonus (fin de série)

Deux phases, dans cet ordre strict :
1. **Mise à l'aveugle** : le joueur choisit un palier (4 options, croissants par série, jusqu'à 3000 pts) **avant** de connaître la question. Pas de choix dans le délai → palier "safe" par défaut.
2. **Question** : morceau révélé, timer fixe **sans dégressivité**, audio ralenti par défaut (`playbackRate` réduit, paramètre désactivable). Un seul essai. Pas de réponse → traité comme faux (perte de la mise).

Après la question bonus de chaque série : le tableau général **peut** s'afficher (au moins une fois par partie, pas forcément à chaque série — par défaut vers la série médiane, ou déclenché manuellement par le host).

## Mode équipes (optionnel)

Si `modeÉquipe` actif : chaque joueur répond toujours individuellement, mais les points vont au score de son équipe (`teamId`), pas à un score individuel.

## Reconnexion joueur et host

- Un `Player` est identifié par un `playerId` **stable**, généré et persisté côté client Flutter — jamais par le `connectionId` SignalR, qui change à chaque reconnexion (coupure WiFi, verrouillage d'écran). `JoinGame(code, nom, playerId)` sert aussi bien à rejoindre qu'à se reconnecter : si le `playerId` existe déjà dans la partie, le serveur réassocie le nouveau `connectionId` et renvoie l'état existant du joueur (score, équipe) plutôt que d'en créer un nouveau.
- Le host (page web PC) peut resynchroniser son état après un refresh/crash via `RejoinAsHost(code)`, qui renvoie l'état courant (morceau, mode, position audio théorique, `enPause`) pour reprendre la lecture sans redémarrer le morceau.

## Pause

Le host peut geler la partie à tout moment (round classique ou bonus). Reprise de l'audio exactement là où il s'était arrêté. Toute soumission reçue pendant `enPause = true` est rejetée côté serveur (ne pas faire confiance uniquement à l'UI client).

## Contraintes non négociables

- Pas de base de données — `tracks.json` chargé en mémoire, source de vérité unique pour les métadonnées.
- `tracks.json` (métadonnées éditoriales) et `stats.json` (compteurs runtime type `playCount`) sont deux fichiers séparés — le backend n'écrit jamais dans `tracks.json`, et le script d'import CSV ne touche jamais à `stats.json`.
- Aucun paramètre de timing/scoring/mise en dur : tout passe par une config par partie/série.
- L'audio ne sort jamais vers les clients joueurs (Flutter) — seul le client host le joue.
- Seul le backend est dockerisé ; jamais le frontend.
- Identification des joueurs par `playerId` stable côté client, jamais par `connectionId` SignalR.

## Quand aller lire `docs/architecture.md`

- Schéma complet de `tracks.json` (champs `genres`, `tags`, `trapWith`, `coverPath`, etc.)
- Contrat SignalR intégral (toutes les méthodes host/joueur et tous les événements serveur)
- Table des paramètres de configuration et leurs valeurs par défaut recommandées
- Détails du déploiement Docker (volumes, docker-compose)
