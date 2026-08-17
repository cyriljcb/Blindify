/// Miroir de `Blindify.Domain.Enums.RoundCible` — ce qui est demandé au joueur pour
/// ce round (tiré aléatoirement côté serveur, voir RoundService.DemarrerRound).
enum RoundCible { titre, auteur }

extension RoundCibleJson on RoundCible {
  static RoundCible fromJson(String value) {
    switch (value) {
      case 'Titre':
        return RoundCible.titre;
      case 'Auteur':
        return RoundCible.auteur;
    }
    throw ArgumentError('RoundCible inconnue reçue du serveur : $value');
  }

  String get label {
    switch (this) {
      case RoundCible.titre:
        return 'le titre';
      case RoundCible.auteur:
        return "l'artiste";
    }
  }
}
