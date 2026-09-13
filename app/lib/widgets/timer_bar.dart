import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';

import '../theme.dart';

/// Décompte chiffré agrandi (police mono, couleur alignée sur la barre) au lieu d'un simple
/// libellé discret sous une fine barre — retour utilisateur : le timer passait inaperçu sur
/// mobile. Partagé par tous les écrans à minuteur (round classique, mise bonus, question bonus).
class TimerBar extends StatelessWidget {
  const TimerBar({super.key, required this.progress, required this.secondesRestantes});

  final double progress;
  final int secondesRestantes;

  @override
  Widget build(BuildContext context) {
    final pct = progress.clamp(0, 1) * 100;
    final color = pct <= 15 ? BlindifyColors.bad : (pct <= 40 ? BlindifyColors.warn : BlindifyColors.accent);

    return TweenAnimationBuilder<Color?>(
      tween: ColorTween(end: color),
      duration: const Duration(milliseconds: 300),
      builder: (context, animatedColor, _) {
        final c = animatedColor ?? BlindifyColors.accent;
        return Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            Expanded(
              child: Container(
                height: 18,
                decoration: BoxDecoration(
                  color: BlindifyColors.surfaceAlt,
                  borderRadius: BorderRadius.circular(999),
                  border: Border.all(color: BlindifyColors.ink, width: 2),
                ),
                clipBehavior: Clip.antiAlias,
                child: FractionallySizedBox(
                  alignment: Alignment.centerLeft,
                  widthFactor: progress.clamp(0, 1),
                  child: DecoratedBox(decoration: BoxDecoration(color: c)),
                ),
              ),
            ),
            const SizedBox(width: 12),
            SizedBox(
              width: 52,
              child: _buildCountdown(secondesRestantes, c, pct),
            ),
          ],
        );
      },
    );
  }

  /// Pulsation continue sous 15 % restants — retour utilisateur : le décompte se contentait de
  /// changer de couleur, pas assez visible pour transmettre l'urgence des dernières secondes.
  /// L'Animate garde son identité d'un tick à l'autre (même position dans l'arbre, pas de clé
  /// changeante) tant que pct reste sous le seuil, donc le contrôleur boucle sans jamais être
  /// recréé toutes les 100ms — il est simplement retiré (et l'anim arrêtée) dès que le seuil est
  /// repassé au-dessus.
  Widget _buildCountdown(int secondesRestantes, Color color, num pct) {
    final text = Text(
      '${secondesRestantes}s',
      textAlign: TextAlign.right,
      style: GoogleFonts.spaceMono(fontSize: 24, fontWeight: FontWeight.w700, color: color),
    );
    if (pct > 15) return text;
    return text
        .animate(onPlay: (controller) => controller.repeat(reverse: true))
        .scaleXY(end: 1.18, duration: const Duration(milliseconds: 420), curve: Curves.easeInOut);
  }
}
