namespace Blindify.Api.Contracts;

/// <summary>Annonce de série (retour utilisateur, playtest 2026-08-24 : l'écran "Série B — Rock"
/// existait déjà côté host/écran public mais jamais côté téléphone, qui n'a par ailleurs jamais eu
/// accès aux tags d'une série — Tags est donc envoyé ici en clair, contrairement à RoundStarted qui
/// ne renvoie que SerieIndex (le host construit déjà son libellé localement depuis ce qu'il a
/// lui-même soumis à CreateGame). Le libellé humain ("Rock", "Aléatoire" si vide) est formaté côté
/// client (voir host/app.js:libelleTheme et son équivalent Flutter) pour rester dans la même langue
/// que le reste de l'UI plutôt que d'être figé côté serveur.</summary>
public record SerieAnnonceeDto(int SerieIndex, List<string> Tags);
