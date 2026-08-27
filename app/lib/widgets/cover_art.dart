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
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: BlindifyColors.ink, width: 3),
        boxShadow: hardShadow(BlindifyColors.mustard),
      ),
      clipBehavior: Clip.antiAlias,
      child: imageUrl == null
          ? const Center(child: Icon(Icons.music_note_rounded, size: 56, color: BlindifyColors.inkDim))
          : Image.network(
              imageUrl!,
              fit: BoxFit.cover,
              errorBuilder: (context, error, stackTrace) =>
                  const Center(child: Icon(Icons.music_note_rounded, size: 56, color: BlindifyColors.inkDim)),
              loadingBuilder: (context, child, progress) =>
                  progress == null ? child : const Center(child: CircularProgressIndicator(strokeWidth: 2)),
            ),
    );
  }
}

/// Pochette "mystère" pendant la découverte (le joueur ne reçoit jamais coverPath avant le
/// reveal — voir game_connection.dart) : disque vinyle qui tourne en continu, jamais d'info
/// sur le morceau. Même motif que le CSS `@keyframes spin-vinyle` côté host, transposé en
/// CustomPainter (grooves concentriques + label) plutôt qu'un dégradé radial CSS.
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
    _controller = AnimationController(vsync: this, duration: const Duration(milliseconds: 3200))..repeat();
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
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: BlindifyColors.ink, width: 3),
      ),
      alignment: Alignment.center,
      child: RotationTransition(
        turns: _controller,
        child: SizedBox(
          width: widget.size * 0.76,
          height: widget.size * 0.76,
          child: CustomPaint(painter: _VinylPainter()),
        ),
      ),
    );
  }
}

/// Sillons concentriques + label central — dessiné une fois par frame de rotation, pas de
/// widgets empilés.
class _VinylPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2;

    canvas.drawCircle(center, radius, Paint()..color = const Color(0xFF050505));

    final groovePaint = Paint()
      ..color = BlindifyColors.surface
      ..style = PaintingStyle.stroke
      ..strokeWidth = radius * 0.045;
    for (var r = radius * 0.32; r < radius * 0.96; r += radius * 0.11) {
      canvas.drawCircle(center, r, groovePaint);
    }

    canvas.drawCircle(center, radius * 0.28, Paint()..color = BlindifyColors.mustard);
    canvas.drawCircle(
      center,
      radius * 0.28,
      Paint()
        ..color = BlindifyColors.ink
        ..style = PaintingStyle.stroke
        ..strokeWidth = radius * 0.05,
    );
    canvas.drawCircle(center, radius * 0.05, Paint()..color = BlindifyColors.ink);
  }

  @override
  bool shouldRepaint(covariant _VinylPainter oldDelegate) => false;
}
