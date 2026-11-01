import 'package:flutter/gestures.dart';
import 'package:flutter/widgets.dart';

/// Appui validé au relâchement même si le doigt a glissé pendant la pression — retour utilisateur
/// (2026-11-01, "il faut appuyer plusieurs fois pour que ça soit pris en compte") : un
/// GestureDetector/InkWell standard annule l'appui dès que le doigt bouge de plus de ~18 dp
/// (kTouchSlop), fréquent avec un appui un peu appuyé ou roulé sur une grande tuile. Aucune limite
/// de glissement ici ; à n'utiliser que hors de toute zone défilable (le défilement gagnerait sinon
/// l'arène des gestes et annulerait l'appui de toute façon).
class AppuiTolerant extends StatelessWidget {
  const AppuiTolerant({
    super.key,
    required this.onTap,
    this.onTapDown,
    this.onTapUp,
    this.onTapCancel,
    required this.child,
  });

  final VoidCallback? onTap;
  final VoidCallback? onTapDown;
  final VoidCallback? onTapUp;
  final VoidCallback? onTapCancel;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    return RawGestureDetector(
      behavior: HitTestBehavior.opaque,
      gestures: {
        TapGestureRecognizer: GestureRecognizerFactoryWithHandlers<TapGestureRecognizer>(
          () => TapGestureRecognizer(debugOwner: this, preAcceptSlopTolerance: null, postAcceptSlopTolerance: null),
          (recognizer) => recognizer
            ..onTap = onTap
            ..onTapDown = (onTapDown == null ? null : (_) => onTapDown!())
            ..onTapUp = (onTapUp == null ? null : (_) => onTapUp!())
            ..onTapCancel = onTapCancel,
        ),
      },
      child: child,
    );
  }
}
