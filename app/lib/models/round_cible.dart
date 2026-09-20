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
