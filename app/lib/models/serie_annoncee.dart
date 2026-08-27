/// Miroir de `SerieAnnonceeDto` — annonce de série diffusée avant le premier round de chaque
/// série (retour utilisateur du 2026-08-24 : existait déjà côté host/écran public, jamais côté
/// téléphone).
class SerieAnnoncee {
  SerieAnnoncee({required this.serieIndex, required this.tags});

  final int serieIndex;
  final List<String> tags;

  factory SerieAnnoncee.fromJson(Map<String, dynamic> json) => SerieAnnoncee(
        serieIndex: json['serieIndex'] as int,
        tags: (json['tags'] as List<dynamic>).cast<String>(),
      );
}
