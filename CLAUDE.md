# Blindify — contexte projet

Blindtest local multijoueur : buzzer/quiz en réseau local, remplace un système type QuizzXpress.

## Stack

| Composant | Techno |
|---|---|
| Backend | ASP.NET Core + SignalR |
| App joueur | Flutter (buzzer, réponses, pas d'audio) |
| Client host | Page web, jouée sur un PC relié à une enceinte |
| Stockage | `data/tracks.json` (métadonnées, source de vérité — **pas de DB**) + `data/stats.json` (compteurs runtime type `playCount`, séparé pour éviter les conflits d'écriture avec le tableur) |
| Audio/covers | Fichiers locaux sur HDD 2 To (Raspberry Pi), téléchargés depuis YouTube + Spotify |
| Déploiement | Backend dockerisé sur le Raspberry Pi, réseau local uniquement, pas d'accès distant |

## Contraintes structurantes (ne pas dévier sans discussion)

- **Tout changement structurel (architecture, stack, schéma de données, contrat SignalR, choix de dépendances, structure du repo) doit être validé avec moi avant d'être implémenté.** En cas de doute sur ce qui compte comme "structurel", demander plutôt que de trancher seul.
- L'audio ne transite **jamais** vers les téléphones — seul le client host (PC) lit l'audio.
- Les réponses sont **indépendantes par joueur**, pas de verrouillage/buzzer exclusif (chacun répond à son rythme, un seul essai par round).
- Tous les timings/scores/paliers sont **configurables par partie ou par série** (`SeriesConfig`), jamais en dur dans le code.
- `tracks.json` reste la source de vérité unique tant que le catalogue ne dépasse pas ~10-20k morceaux. Ne jamais y écrire depuis le backend — les compteurs runtime (`playCount`) vivent dans `data/stats.json`, séparé.
- Les joueurs sont identifiés par un `playerId` stable (généré côté Flutter), jamais par le `connectionId` SignalR — indispensable pour survivre à une coupure réseau sans perdre le score du joueur.
- Pénalité d'une mauvaise réponse (round classique) = `pointsEnJeu × 0.5`, pas symétrique au gain — voir `docs/architecture.md` section 6 pour la justification (éviter que les joueurs hésitants s'abstiennent systématiquement).
- **Seul le backend est dockerisé.** Le frontend (Flutter + page web host) ne l'est jamais — clients natifs qui consomment le backend par le réseau.
- Les données (`tracks.json`, audio, covers) sont **montées en volume**, jamais copiées dans l'image Docker.

## Documentation

Le détail complet (modèle de données, cycle de vie des rounds, formules de scoring, contrat SignalR, question bonus, mode équipes) est dans **`docs/architecture.md`**. Le lire avant toute modification touchant les règles de jeu, le hub SignalR, ou le schéma `tracks.json`.

## Structure du repo

```
blindify/
├── CLAUDE.md
├── docs/architecture.md
├── backend/                      ← solution ASP.NET Core (clean archi)
│   ├── Blindify.sln
│   ├── src/
│   │   ├── Blindify.Domain/          ← entités pures, pas de dépendance externe
│   │   ├── Blindify.Application/     ← logique métier (scoring, rounds, QCM, Levenshtein)
│   │   ├── Blindify.Infrastructure/  ← tracks.json, fichiers audio/covers
│   │   └── Blindify.Api/             ← Program.cs, GameHub (SignalR), DI
│   └── tests/Blindify.Tests/
├── app/                           ← Flutter
├── data/
│   ├── tracks.json
│   └── scripts/                  ← sync Spotify, téléchargement YouTube, export/import CSV
└── .claude/skills/blindify-rules/SKILL.md
```

`Domain`/`Application` ne référencent aucun package ASP.NET Core — logique de jeu testable indépendamment du framework web.

## Commandes

*(à compléter une fois les projets initialisés — build/run/test backend et Flutter)*
