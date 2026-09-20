/// Miroir de `TitreDto` (V2, section 12.6) — même DTO host/joueurs, aucun secret : `description`
/// contient déjà la valeur mesurée (ex. "2,4 s en moyenne").
class Titre {
  Titre({required this.code, required this.libelle, required this.description, required this.playerIds});

  final String code;
  final String libelle;
  final String description;
  final List<String> playerIds;

  factory Titre.fromJson(Map<String, dynamic> json) => Titre(
        code: json['code'] as String,
        libelle: json['libelle'] as String,
        description: json['description'] as String,
        playerIds: (json['playerIds'] as List<dynamic>).cast<String>(),
      );
}
