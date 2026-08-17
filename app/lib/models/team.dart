class Team {
  Team({required this.id, required this.nom});

  final String id;
  final String nom;

  factory Team.fromJson(Map<String, dynamic> json) => Team(
        id: json['id'] as String,
        nom: json['nom'] as String,
      );
}
