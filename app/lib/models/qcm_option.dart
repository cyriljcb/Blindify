/// Option QCM telle qu'envoyée par le serveur (voir QcmOptionDto côté backend) : un identifiant
/// opaque, à renvoyer tel quel comme réponse, et le seul texte à afficher pour la cible du round
/// (titre, premier artiste crédité ou film — déjà calculé côté serveur). Audit 2026-11-10 : plus
/// de TrackId, qui permettait de télécharger le MP3 de chaque option et d'en déduire la réponse.
class QcmOption {
  QcmOption({required this.optionId, required this.texte});

  final String optionId;
  final String texte;

  factory QcmOption.fromJson(Map<String, dynamic> json) => QcmOption(
        optionId: json['optionId'] as String,
        texte: json['texte'] as String,
      );
}
