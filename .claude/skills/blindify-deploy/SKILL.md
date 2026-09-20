---
name: blindify-deploy
description: Procédure de déploiement de Blindify sur le Raspberry Pi de production (backend Docker + APK Android + host web). Utiliser CE SKILL dès que l'utilisateur demande de déployer, pousser sur le Pi, mettre à jour/redémarrer le serveur, rebuild ou redéployer l'APK, ou vérifier que le Pi tourne la dernière version — même sans le mot "déploiement" explicite (ex. "envoie ça sur le Pi", "les joueurs ont la dernière version ?").
---

# Déploiement Blindify — Raspberry Pi de production

## Accès

- SSH : `ssh cyril@pi.local` — clé déjà configurée (`~/.ssh/id_ed25519` côté poste de dev), `BatchMode=yes` fonctionne sans interaction. Alias connus dans `known_hosts` : `pi.local`, `raspberrypi-local`. Utiliser `pi.local` (mDNS) plutôt qu'une IP — l'IP DHCP du Pi peut changer.
- Projet sur le Pi : `~/apps/blindify` — clone git de `git@github.com:cyriljcb/Blindify.git`, branche `master`.
- Données runtime : `~/apps/blindify/data/` (chemin réel défini par `BLINDIFY_DATA_ROOT` dans `~/apps/blindify/.env` sur le Pi — **ne pas supposer** `/mnt/hdd2to/blindify`, c'est seulement le défaut documenté dans `docker-compose.yml`, écrasé ici par `.env`).
- Secrets (mots de passe admin/restart) : dans `~/apps/blindify/.env` sur le Pi. Ne jamais les lire à voix haute dans le chat ni les recopier dans ce skill, un commit, ou un fichier du repo.
- Backend accessible en HTTP depuis le LAN du poste de dev : `http://pi.local:5000` (CORS ouvert, `AllowAnyOrigin`).
- Conteneur : `docker compose` (service `backend`, conteneur `blindify-backend`). `docker compose version` sur le Pi : v5.4.0 (syntaxe `docker compose ...`, pas `docker-compose ...`).

## Architecture de déploiement (ce qui suit quel canal)

| Composant | Canal | Rebuild nécessaire ? |
|---|---|---|
| Backend (`backend/`) | `git pull` + `docker compose up -d --build` | Oui (image Docker) |
| Host web (`host/*.js`, `*.html`, `*.css`) | `git pull` seul | Non — servi en fichiers statiques (bind-mount `./host:/host:ro`, relatif au repo) |
| APK Android (`host/blindify.apk`, `apk_version.json`) | **PAS git** (gitignoré) — `scp` depuis le poste de dev | Oui, en LOCAL (`flutter build apk`), jamais sur le Pi |
| `tracks.json` / `flags_resolutions.json` | Scripts `data/scripts/` uniquement | — (le backend ne les écrit jamais, voir CLAUDE.md) |

## Procédure — déployer un changement backend/host

1. Vérifier tests + `git status` en local. Ne committer que si l'utilisateur l'a demandé.
2. `git push origin master`.
3. `ssh cyril@pi.local "cd ~/apps/blindify && git pull origin master"`.
4. **Avant** le rebuild : si le changement introduit un nouveau fichier monté en volume (voir `docker-compose.yml`), le créer côté Pi s'il n'existe pas (`echo '[]' > data/xxx.json` ou `echo '{}' > ...`) — un bind-mount Docker sur un chemin absent côté hôte devient un DOSSIER vide côté conteneur, ce qui fait planter le backend à la première écriture dedans.
5. `ssh cyril@pi.local "cd ~/apps/blindify && docker compose up -d --build"`.
6. Vérifier : `docker compose ps` (Up, pas de restart loop), `docker compose logs --tail=30 backend` (pas de `fail:`), `curl -s -o /dev/null -w '%{http_code}\n' http://localhost:5000/api/tags` (200).
7. Si le changement touche la logique de jeu (rounds, scoring, contrat SignalR) : faire une partie test (voir plus bas) avant de considérer le déploiement terminé — un simple démarrage du conteneur ne garantit pas que `StartRound`/etc. fonctionnent réellement (voir incident ci-dessous).

## Procédure — déployer l'app Flutter (APK)

1. Bumper `app/pubspec.yaml` (`version: X.Y.Z+N` → `X.Y.(Z+1)+(N+1)`, toujours +1 sur les deux nombres, jamais de saut) — commit séparé `chore(app): bump version X.Y.Z+N`, push, puis `git pull` côté Pi aussi (pour rester synchronisé, même si ce fichier n'affecte pas le backend).
2. Depuis `app/` en LOCAL (pas sur le Pi — pas de Flutter SDK là-bas) : `./scripts/build_release.ps1`. Lit la version depuis `pubspec.yaml`, build l'APK release, copie vers `host/blindify.apk` + écrit `host/apk_version.json`.
3. `scp host/blindify.apk host/apk_version.json cyril@pi.local:~/apps/blindify/host/`.
4. Vérifier : `curl -s http://pi.local:5000/apk_version.json` (bon numéro de version) et `curl -s -o /dev/null -w '%{http_code}\n' http://pi.local:5000/blindify.apk` (200).
5. **Ne jamais committer** `host/blindify.apk` / `host/apk_version.json` (gitignorés — état de déploiement, pas du code source).
6. **Ne jamais rebuild/redéployer l'APK sans que ce soit explicitement demandé** — voir mémoire `feedback_apk_rebuild` : un rebuild local qui ne part pas sur le Pi ne sert à rien.

## Piège connu — écriture atomique et bind mounts individuels (incident du 2026-09-20)

`stats.json` et `flags.json` sont montés **fichier par fichier** dans `docker-compose.yml` (pas le dossier `data/` entier en un seul volume). Une écriture "atomique" par `File.Move` (rename) échoue avec `Device or resource busy` (EBUSY) : on ne peut pas remplacer l'inode d'un point de montage par `rename()` depuis l'intérieur du conteneur — confirmé en conditions réelles (`docker exec ... mv` échoue, `cp` réussit sur le même chemin).

`AtomicJsonFile.Write` (`backend/src/Blindify.Infrastructure/Persistence/`) utilise donc `File.Copy` + `File.Delete` du temporaire, jamais `File.Move`, pour tout fichier qui finit dans `data/` et est monté individuellement. **Ne jamais revenir à `File.Move`/rename pour ces fichiers** — ce bug a fait planter `StartRound` en production dès le premier round (`IncrementPlayCount` → `AtomicJsonFile.Write` → EBUSY) avant d'être découvert et corrigé via une vraie partie test.

## Piège connu — SixLabors.ImageSharp et `dotnet publish -c Release` (incident du 2026-09-20)

À partir de la version 4.0.0, le paquet `SixLabors.ImageSharp` embarque une tâche MSBuild
(`SixLabors.Licensing`) qui **échoue en erreur dure** sans clé de licence enregistrée sur
sixlabors.com — mais seulement en configuration Release (`ContinueOnError` dépend de
`$(Configuration)` dans les `.targets` du paquet). `dotnet build`/`dotnet test` en local (Debug) ne
faisaient donc qu'avertir, alors que le `Dockerfile` du Pi (`dotnet publish -c Release`) plantait
dès la première tentative de build de l'image — le bug n'était visible qu'au moment du déploiement,
jamais en local. Corrigé en épinglant `SixLabors.ImageSharp` sur **3.1.12** (dernière version 3.x,
avant l'introduction de cette tâche) dans `backend/src/Blindify.Api/Blindify.Api.csproj` — même
licence (Six Labors Split), même conformité gratuite pour Blindify, juste sans le blocage. **Pour
toute future dépendance NuGet touchant à des paquets avec un modèle de licence commercial/split** :
valider un `dotnet publish -c Release` en local (pas seulement `dotnet build`/`dotnet test`) avant
de pousser sur le Pi.

## Piège connu — scripts PowerShell et encodage

Les `.ps1` du repo (ex. `app/scripts/build_release.ps1`) contiennent des accents et doivent avoir un **BOM UTF-8**, sinon Windows PowerShell 5.1 (le shell réellement utilisé ici, pas PowerShell Core) corrompt les caractères multi-octets et peut casser le parseur en plein milieu d'une chaîne. `-Encoding utf8NoBOM` n'existe QUE dans PowerShell Core — en 5.1, utiliser `ascii` (contenu pur ASCII, cas de `apk_version.json`) ou `UTF8` (ajoute un BOM, sans risque pour du JSON).

## Faire une partie test après un déploiement backend

Pas de client web joueur ni d'émulateur Android disponibles directement depuis une session Claude Code. Écrire un petit client SignalR jetable en C# (`Microsoft.AspNetCore.SignalR.Client`, même version que `backend/tests/Blindify.Tests/Blindify.Tests.csproj`), dans le dossier scratchpad de la session — **jamais commité, jamais dans le repo**. Pointer sur `http://pi.local:5000/hubs/game`.

Scénario minimal à exercer :
1. `CreateGame` → `ConfigurerPartie` (quelques rounds courts, thèmes vides = catalogue entier).
2. `JoinGame` d'un joueur de test.
3. Pour chaque round : `StartRound` → attendre `RoundStarted` (host + joueur) → `SubmitAnswer` → attendre `RoundEnded` → `NextRound` (sauf dernier round — sans `NextRound`, `StartRound` rejoue toujours le round 0).
4. `StartBonusRound` → attendre `BonusStakeOptions` → `SelectStake` → attendre `BonusQuestionStarted` → `SubmitBonusAnswer` → attendre `BonusResult`.
5. Reconnexion automatique (V2) : fermer la connexion joueur, en rouvrir une nouvelle avec `?code=...&playerId=...` dans l'URL, vérifier la réception de l'évènement `EtatCourant`.
6. `EndGame` → vérifier que `GameEnded` contient `titres` (au moins 1, `FIDELE` en repli).
7. `SignalerMorceau` sur un morceau réellement joué pendant le test → vérifier `dejaSignale=false` puis un second appel identique → `dejaSignale=true` → vérifier que l'évènement `MorceauSignale` arrive bien côté host.

**Règle de sécurité — ne jamais tester une hypothèse système (comportement de `mv`/`cp`, permissions, etc.) avec des commandes shell brutes directement sur `data/stats.json` ou `data/flags.json` réels.** Ça écrase les vraies données de jeu en production (vécu : un `mv`/`cp` de diagnostic a effacé le vrai contenu de `stats.json`, restauré ensuite depuis une copie locale approximative — perte de données réelle, pas juste théorique). Sauvegarder d'abord (`cp data/stats.json data/stats.json.backup-avant-test`) si un test doit absolument toucher ces fichiers directement, supprimer la sauvegarde une fois le test validé, et **le dire clairement à l'utilisateur si un incident survient quand même** plutôt que de le corriger silencieusement.

Après un test qui a écrit dans `flags.json` avec un signalement factice : le supprimer du fichier ET redémarrer le conteneur (`docker compose restart backend`). `FlagsRepository` charge `flags.json` une seule fois au démarrage et garde son propre état en mémoire — éditer le fichier à la main sans redémarrer ne suffit pas, le prochain vrai signalement réécrirait l'ancienne entrée de test depuis la mémoire du process.

## Rappel — CLAUDE.md reste la référence pour les contraintes non négociables

Ce skill couvre le déploiement. Pour les règles de jeu, le contrat SignalR, ou le schéma `tracks.json`, voir le skill `blindify-rules` et `docs/architecture.md`.
