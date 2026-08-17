import 'package:flutter/material.dart';

import '../theme.dart';

/// Pochette d'album au reveal (round classique ou question bonus) — `imageUrl` null avant la
/// révélation ou si le morceau n'a pas de coverPath, auquel cas un icône générique est affiché.
class CoverArt extends StatelessWidget {
  const CoverArt({super.key, this.imageUrl, this.size = 180});

  final String? imageUrl;
  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: BlindifyColors.surfaceAlt,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BlindifyColors.border),
        boxShadow: [
          BoxShadow(color: Colors.black.withValues(alpha: 0.4), blurRadius: 30, offset: const Offset(0, 14)),
        ],
      ),
      clipBehavior: Clip.antiAlias,
      child: imageUrl == null
          ? const Center(child: Icon(Icons.music_note_rounded, size: 56, color: BlindifyColors.textDim))
          : Image.network(
              imageUrl!,
              fit: BoxFit.cover,
              errorBuilder: (context, error, stackTrace) =>
                  const Center(child: Icon(Icons.music_note_rounded, size: 56, color: BlindifyColors.textDim)),
              loadingBuilder: (context, child, progress) =>
                  progress == null ? child : const Center(child: CircularProgressIndicator(strokeWidth: 2)),
            ),
    );
  }
}

/// Pochette "mystère" pendant la découverte (le joueur ne reçoit jamais coverPath avant le
/// reveal — voir game_connection.dart) : icône animée en pulsation plutôt qu'une vraie image.
class MysteryCoverArt extends StatefulWidget {
  const MysteryCoverArt({super.key, this.size = 140});

  final double size;

  @override
  State<MysteryCoverArt> createState() => _MysteryCoverArtState();
}

class _MysteryCoverArtState extends State<MysteryCoverArt> with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: const Duration(milliseconds: 1400))..repeat(reverse: true);
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      width: widget.size,
      height: widget.size,
      decoration: BoxDecoration(
        color: BlindifyColors.surfaceAlt,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: BlindifyColors.border),
      ),
      child: FadeTransition(
        opacity: Tween(begin: 0.25, end: 0.6).animate(CurvedAnimation(parent: _controller, curve: Curves.easeInOut)),
        child: ScaleTransition(
          scale: Tween(begin: 1.0, end: 1.08).animate(CurvedAnimation(parent: _controller, curve: Curves.easeInOut)),
          child: Center(child: Text('❓', style: TextStyle(fontSize: widget.size * 0.35))),
        ),
      ),
    );
  }
}
