import 'package:flutter/material.dart';

/// Empile [itemCount] tuiles pour occuper exactement la hauteur disponible, jamais plus — à la
/// place d'un ListView scrollable dont le contenu peut dépasser l'espace visible selon l'écran et
/// obliger à scroller pour voir toutes les options (retour utilisateur, QCM et paliers de mise
/// bonus). Écrans concernés : peu d'items (4 au maximum aujourd'hui), donc les tuiles restent
/// lisibles même compressées ; ne pas utiliser pour une liste dont la taille n'est pas bornée.
class FillHeightList extends StatelessWidget {
  const FillHeightList({super.key, required this.itemCount, required this.itemBuilder, this.spacing = 12});

  final int itemCount;
  final Widget Function(BuildContext context, int index) itemBuilder;
  final double spacing;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final tileHeight = itemCount > 0 ? (constraints.maxHeight - spacing * (itemCount - 1)) / itemCount : 0.0;

        return Column(
          children: [
            for (var index = 0; index < itemCount; index++) ...[
              if (index > 0) SizedBox(height: spacing),
              SizedBox(height: tileHeight, child: itemBuilder(context, index)),
            ],
          ],
        );
      },
    );
  }
}
