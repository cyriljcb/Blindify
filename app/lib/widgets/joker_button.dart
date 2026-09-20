import 'package:flutter/material.dart';

import '../theme.dart';

/// Bouton du joker (V2, section 12.7) — appui long (~0,6 s) avec un anneau qui se remplit, pas de
/// boîte de dialogue (le chrono du round continue). Contour moutarde tant que disponible, grisé et
/// barré une fois utilisé.
class JokerButton extends StatefulWidget {
  const JokerButton({super.key, required this.disponible, required this.onActiver});

  final bool disponible;
  final VoidCallback onActiver;

  @override
  State<JokerButton> createState() => _JokerButtonState();
}

class _JokerButtonState extends State<JokerButton> with SingleTickerProviderStateMixin {
  static const _duree = Duration(milliseconds: 600);

  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: _duree)
      ..addStatusListener((status) {
        if (status == AnimationStatus.completed) {
          widget.onActiver();
          _controller.reset();
        }
      });
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return GestureDetector(
      onLongPressStart: widget.disponible ? (_) => _controller.forward() : null,
      onLongPressCancel: () => _controller.reverse(),
      onLongPressEnd: (_) => _controller.reverse(),
      child: SizedBox(
        width: 40,
        height: 40,
        child: Stack(
          alignment: Alignment.center,
          children: [
            const CircleBorderOutline(),
            if (widget.disponible)
              AnimatedBuilder(
                animation: _controller,
                builder: (context, _) => CircularProgressIndicator(
                  value: _controller.value,
                  strokeWidth: 3,
                  color: BlindifyColors.mustard,
                  backgroundColor: Colors.transparent,
                ),
              ),
            Opacity(
              opacity: widget.disponible ? 1 : 0.4,
              child: Icon(
                Icons.auto_awesome_rounded,
                size: 20,
                color: widget.disponible ? BlindifyColors.mustard : BlindifyColors.inkDim,
              ),
            ),
            if (!widget.disponible)
              Transform.rotate(
                angle: -0.785398, // -45°
                child: Container(width: 2, height: 28, color: BlindifyColors.inkDim),
              ),
          ],
        ),
      ),
    );
  }
}

/// Simple anneau statique — état "disponible mais pas en train d'appuyer" (CircularProgressIndicator
/// à value:0 ne dessine rien de visible, il faut un contour dédié).
class CircleBorderOutline extends StatelessWidget {
  const CircleBorderOutline({super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(shape: BoxShape.circle, border: Border.fromBorderSide(BorderSide(color: BlindifyColors.borderSoft, width: 3))),
    );
  }
}
