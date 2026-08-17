class QcmOption {
  QcmOption({required this.trackId, required this.title, required this.artist});

  final String trackId;
  final String title;
  final String artist;

  factory QcmOption.fromJson(Map<String, dynamic> json) => QcmOption(
        trackId: json['trackId'] as String,
        title: json['title'] as String,
        artist: json['artist'] as String,
      );
}
