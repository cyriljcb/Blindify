import 'package:flutter/material.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';

import '../motion.dart';
import '../theme.dart';

/// Forme associée à une position d'option QCM — identique sur l'écran public (host/display.js,
/// même ordre) pour que le joueur retrouve son choix d'un coup d'œil sur la TV, et lisible sans
/// distinguer les couleurs (daltonisme). Refonte UI, lot 1.
enum FormeQcm { triangle, losange, rond, carre }

class StyleOptionQcm {
  const StyleOptionQcm(this.fond, this.texte, this.forme);

  final Color fond;
  final Color texte;
  final FormeQcm forme;
}

/// Toujours indexé sur la position de l'option dans la liste COMPLÈTE envoyée par le serveur, pas
/// sur la liste filtrée par le joker : une option garde sa couleur/forme après un 50/50, comme sur
/// la TV qui continue d'afficher les 4.
StyleOptionQcm styleOptionQcm(int index) => switch (index % 4) {
      0 => const StyleOptionQcm(BlindifyColors.coral, BlindifyColors.ink, FormeQcm.triangle),
      1 => const StyleOptionQcm(BlindifyColors.cobalt, BlindifyColors.ink, FormeQcm.losange),
      2 => const StyleOptionQcm(BlindifyColors.mustard, BlindifyColors.onLight, FormeQcm.rond),
      _ => const StyleOptionQcm(BlindifyColors.good, BlindifyColors.onLight, FormeQcm.carre),
    };

enum EtatTuileQcm { normale, choisie, estompee }

/// Tuile de réponse QCM colorée. [etat] : `choisie` = cadre blanc épais + étiquette « Verrouillé »
/// (pas de vert, réservé au résultat — la tuile ■ est elle-même verte), `estompee` = les autres
/// options une fois la réponse envoyée.
class QcmTile extends StatefulWidget {
  const QcmTile({
    super.key,
    required this.label,
    required this.index,
    required this.ordreApparition,
    required this.etat,
    required this.onPressed,
  });

  final String label;

  /// Position dans la liste complète du serveur — détermine couleur et forme.
  final int index;

  /// Position à l'écran — ne sert qu'au décalage de l'animation d'entrée.
  final int ordreApparition;
  final EtatTuileQcm etat;
  final VoidCallback? onPressed;

  @override
  State<QcmTile> createState() => _QcmTileState();
}

class _QcmTileState extends State<QcmTile> {
  bool _pressed = false;

  void _setPressed(bool value) {
    if (widget.onPressed == null) return;
    setState(() => _pressed = value);
  }

  @override
  Widget build(BuildContext context) {
    final style = styleOptionQcm(widget.index);
    final choisie = widget.etat == EtatTuileQcm.choisie;

    final tuile = AnimatedOpacity(
      opacity: widget.etat == EtatTuileQcm.estompee ? 0.3 : 1,
      duration: BlindifyMotion.fast,
      child: AnimatedContainer(
        duration: BlindifyMotion.fast,
        width: double.infinity,
        height: double.infinity,
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
        decoration: BoxDecoration(
          color: style.fond,
          borderRadius: BorderRadius.circular(10),
          border: Border.all(color: BlindifyColors.ink, width: choisie ? 4 : 2),
          boxShadow: widget.etat == EtatTuileQcm.estompee ? const [] : hardShadow(BlindifyColors.ink, offset: 3),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                SizedBox.square(dimension: 20, child: CustomPaint(painter: _FormePainter(style.forme, style.texte))),
                const Spacer(),
                if (choisie) const _EtiquetteVerrouille(),
              ],
            ),
            Expanded(
              child: Align(
                alignment: Alignment.bottomLeft,
                child: Text(
                  widget.label,
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16, height: 1.15, color: style.texte),
                ),
              ),
            ),
          ],
        ),
      ),
    );

    return GestureDetector(
      onTapDown: (_) => _setPressed(true),
      onTapUp: (_) => _setPressed(false),
      onTapCancel: () => _setPressed(false),
      onTap: widget.onPressed,
      child: AnimatedScale(
        scale: _pressed ? 0.94 : (choisie ? 1.02 : 1.0),
        duration: BlindifyMotion.fast,
        curve: Curves.easeOut,
        child: tuile,
      ),
    )
        .animate(delay: Duration(milliseconds: 60 * widget.ordreApparition))
        .fadeIn(duration: BlindifyMotion.normal)
        .slideY(begin: 0.25, curve: BlindifyMotion.pop);
  }
}

class _EtiquetteVerrouille extends StatelessWidget {
  const _EtiquetteVerrouille();

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(color: BlindifyColors.ink, borderRadius: BorderRadius.circular(4)),
      child: Text(
        'VERROUILLÉ',
        style: GoogleFonts.spaceMono(fontSize: 10, fontWeight: FontWeight.w700, color: BlindifyColors.onLight, letterSpacing: 0.6),
      ),
    ).animate().fadeIn(duration: BlindifyMotion.fast).scale(begin: const Offset(0.7, 0.7), curve: BlindifyMotion.pop);
  }
}

/// Dessine la forme pleine, mêmes proportions que les SVG de l'écran public (host/display.js).
class _FormePainter extends CustomPainter {
  _FormePainter(this.forme, this.couleur);

  final FormeQcm forme;
  final Color couleur;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()..color = couleur;
    final w = size.width;
    final h = size.height;
    switch (forme) {
      case FormeQcm.triangle:
        canvas.drawPath(Path()..moveTo(w / 2, h * 0.1)..lineTo(w * 0.95, h * 0.9)..lineTo(w * 0.05, h * 0.9)..close(), paint);
      case FormeQcm.losange:
        canvas.drawPath(Path()..moveTo(w / 2, 0)..lineTo(w, h / 2)..lineTo(w / 2, h)..lineTo(0, h / 2)..close(), paint);
      case FormeQcm.rond:
        canvas.drawCircle(Offset(w / 2, h / 2), w * 0.44, paint);
      case FormeQcm.carre:
        canvas.drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(w * 0.1, h * 0.1, w * 0.8, h * 0.8), const Radius.circular(2)), paint);
    }
  }

  @override
  bool shouldRepaint(_FormePainter oldDelegate) => oldDelegate.forme != forme || oldDelegate.couleur != couleur;
}

/// Grille des options QCM : 2×2 quand les 4 options sont affichées (même disposition que la TV),
/// une colonne sinon (après un 50/50 du joker). Remplit toute la hauteur disponible.
class GrilleQcm extends StatelessWidget {
  const GrilleQcm({super.key, required this.tuiles});

  final List<Widget> tuiles;

  static const _espacement = 10.0;

  @override
  Widget build(BuildContext context) {
    if (tuiles.length == 4) {
      return Column(
        children: [
          Expanded(child: Row(children: [Expanded(child: tuiles[0]), const SizedBox(width: _espacement), Expanded(child: tuiles[1])])),
          const SizedBox(height: _espacement),
          Expanded(child: Row(children: [Expanded(child: tuiles[2]), const SizedBox(width: _espacement), Expanded(child: tuiles[3])])),
        ],
      );
    }
    return Column(
      children: [
        for (var i = 0; i < tuiles.length; i++) ...[
          if (i > 0) const SizedBox(height: _espacement),
          Expanded(child: tuiles[i]),
        ],
      ],
    );
  }
}
