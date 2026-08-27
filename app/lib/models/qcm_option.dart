class QcmOption {
  QcmOption({required this.trackId, required this.title, required this.artist, required this.film});

  final String trackId;
  final String title;
  final String artist;

  /// Nom du film d'origine (cible Film, morceaux "disney") — voir FilmNameResolver côté backend.
  final String film;

  factory QcmOption.fromJson(Map<String, dynamic> json) => QcmOption(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
        film: json['film'] as String,
      );
}
