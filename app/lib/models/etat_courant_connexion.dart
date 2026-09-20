import 'etat_courant_joueur.dart';

/// Miroir de `EtatCourantConnexionDto` — envoyé (V2) par le serveur dès qu'une connexion est
/// automatiquement rattachée à un joueur existant via l'URL du hub (?code&playerId, voir
/// GameHub.OnConnectedAsync), sans passer par un nouvel appel explicite à JoinGame. Contenu réduit
/// par rapport à JoinResult : pas de roster/teams à renvoyer, déjà connus du client depuis le join
/// initial.
class EtatCourantConnexion {
  EtatCourantConnexion({required this.score, this.teamId, this.etatCourant});

  final int score;
  final String? teamId;
  final EtatCourantJoueur? etatCourant;

  factory EtatCourantConnexion.fromJson(Map<String, dynamic> json) => EtatCourantConnexion(
        score: json['score'] as int,
        teamId: json['teamId'] as String?,
        etatCourant:
            json['etatCourant'] != null ? EtatCourantJoueur.fromJson(json['etatCourant'] as Map<String, dynamic>) : null,
      );
}
