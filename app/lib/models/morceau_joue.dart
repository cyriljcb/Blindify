/// Morceau déjà joué ET révélé dans la partie courante (construit côté client depuis RoundEnded/
/// BonusResult, jamais un DTO serveur dédié) — alimente la liste « Morceaux joués dans cette partie »
/// des réglages admin (V2, section 12.4). Ne contient jamais un morceau pas encore révélé : l'admin
/// est aussi un joueur, lui montrer le morceau avant le reveal lui donnerait la réponse.
class MorceauJoue {
  MorceauJoue({required this.trackId, required this.titre, required this.artiste});

  final String trackId;
  final String titre;
  final String artiste;
}
