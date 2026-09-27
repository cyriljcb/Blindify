/// Miroir de `Blindify.Domain.Enums.RoundCible` — ce qui est demandé au joueur pour
/// ce round (tiré aléatoirement côté serveur, voir RoundService.DemarrerRound).
/// `film` est forcée pour les morceaux "disney" (titre réel/auteur crédité imprévisibles).
/// `annee` (V2, section 12.5) : éligible seulement si Track.Year est connu côté serveur.
enum RoundCible { titre, auteur, film, annee }

extension RoundCibleJson on RoundCible {
  static RoundCible fromJson(String value) {
    switch (value) {
      case 'Titre':
        return RoundCible.titre;
      case 'Auteur':
        return RoundCible.auteur;
      case 'Film':
        return RoundCible.film;
      case 'Annee':
        return RoundCible.annee;
    }
    throw ArgumentError('RoundCible inconnue reçue du serveur : $value');
  }

  /// La question telle qu'on la pose à voix haute, affichée en très gros pendant la phase de
  /// réponse — même formulation que l'écran public (host/shared/format.js:questionCible).
  String get question {
    switch (this) {
      case RoundCible.titre:
        return 'Quel titre ?';
      case RoundCible.auteur:
        return 'Qui chante ?';
      case RoundCible.film:
        return 'Quel film ?';
      case RoundCible.annee:
        return 'Quelle année ?';
    }
  }

  String get label {
    switch (this) {
      case RoundCible.titre:
        return 'le titre';
      case RoundCible.auteur:
        return "l'artiste";
      case RoundCible.film:
        return 'le film';
      case RoundCible.annee:
        return "l'année";
    }
  }
}
