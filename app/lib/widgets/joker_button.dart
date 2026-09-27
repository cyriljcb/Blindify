import 'dart:async';

import 'package:flutter/material.dart';

import '../theme.dart';

/// Bouton du joker (V2, section 12.7) — retour utilisateur (2026-09-27) : l'ancienne version (icône
/// seule, appui long ~1,1 s en comptant le délai de détection de Flutter) n'était jamais découverte
/// ("je ne sais même pas comment je suis censé l'utiliser"). Désormais un bouton libellé « Joker » :
/// un premier appui l'arme (« Confirmer ? »), un second appui dans les [_delaiConfirmation]
/// l'active. Pas de boîte de dialogue — le chrono du round continue. Grisé et barré une fois utilisé.
class JokerButton extends StatefulWidget {
  const JokerButton({super.key, required this.disponible, required this.onActiver});

  final bool disponible;
  final VoidCallback onActiver;

  @override
  State<JokerButton> createState() => _JokerButtonState();
}

class _JokerButtonState extends State<JokerButton> {
  static const _delaiConfirmation = Duration(seconds: 3);

  bool _arme = false;
  Timer? _desarmement;

  @override
  void dispose() {
    _desarmement?.cancel();
    super.dispose();
  }

  void _onTap() {
    if (!widget.disponible) return;
    if (_arme) {
      _desarmement?.cancel();
      setState(() => _arme = false);
      widget.onActiver();
      return;
    }
    setState(() => _arme = true);
    _desarmement?.cancel();
    _desarmement = Timer(_delaiConfirmation, () {
      if (mounted) setState(() => _arme = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    final disponible = widget.disponible;
    final couleurTexte = !disponible
        ? BlindifyColors.inkDim
        : _arme
            ? BlindifyColors.onLight
            : BlindifyColors.mustard;

    return Semantics(
      button: true,
      label: disponible ? (_arme ? 'Confirmer le joker' : 'Utiliser le joker') : 'Joker déjà utilisé',
      child: GestureDetector(
        onTap: disponible ? _onTap : null,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 150),
          height: 40,
          padding: const EdgeInsets.symmetric(horizontal: 12),
          decoration: BoxDecoration(
            color: _arme && disponible ? BlindifyColors.mustard : Colors.transparent,
            borderRadius: BorderRadius.circular(20),
            border: Border.all(color: disponible ? BlindifyColors.mustard : BlindifyColors.borderSoft, width: 2),
          ),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.auto_awesome_rounded, size: 18, color: couleurTexte),
              const SizedBox(width: 6),
              Text(
                !disponible ? 'Joker utilisé' : (_arme ? 'Confirmer ?' : 'Joker'),
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                  color: couleurTexte,
                  decoration: disponible ? null : TextDecoration.lineThrough,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
