import 'package:flutter/material.dart';
import 'package:google_fonts/google_fonts.dart';

/// Palette partagée avec host/style.css — identité "affiche de concert / pochette vinyle"
/// (fond mat, bordures encre épaisses, ombres dures décalées) des deux côtés de la partie.
class BlindifyColors {
  BlindifyColors._();

  static const bg = Color(0xFF121014);
  static const surface = Color(0xFF1C1A1F);
  static const surfaceAlt = Color(0xFF242229);
  static const ink = Color(0xFFF2EDE1);
  static const inkDim = Color(0xFF8A8578);
  static const borderSoft = Color(0xFF322F36);

  static const coral = Color(0xFFFF4B3E);
  static const cobalt = Color(0xFF3D5AFF);
  static const mustard = Color(0xFFFFC93C);
  static const good = Color(0xFF2ED573);
  static const bad = coral;
  static const warn = mustard;
  static const accent = cobalt;
  static const accent2 = mustard;

  /// Texte clair sur fond saturé (boutons cobalt/coral) — cf `button { color: var(--ink) }`.
  static const onAccent = ink;

  /// Texte sombre sur fond clair (mustard/good — bandeau pause, bouton reprendre, 1ère place).
  static const onLight = bg;
}

ThemeData buildBlindifyTheme() {
  final colorScheme = ColorScheme.fromSeed(
    seedColor: BlindifyColors.cobalt,
    brightness: Brightness.dark,
  ).copyWith(
    primary: BlindifyColors.cobalt,
    onPrimary: BlindifyColors.onAccent,
    secondary: BlindifyColors.mustard,
    onSecondary: BlindifyColors.onLight,
    surface: BlindifyColors.surface,
    onSurface: BlindifyColors.ink,
    error: BlindifyColors.coral,
    outline: BlindifyColors.ink,
  );

  // Rayons resserrés (6-10px, cf --radius/--radius-lg du CSS) — pas les coins très arrondis
  // façon Material de l'ancien thème.
  const radius = 8.0;
  final borderRadius = BorderRadius.circular(radius);

  // Anton (titres condensés en majuscules) + Space Grotesk (texte courant) via Google Fonts,
  // comme host/style.css. Téléchargées au runtime (pas d'assets locaux) : acceptable ici car
  // c'est le téléphone du joueur qui installe l'app (réseau dispo à ce moment-là), à la
  // différence du PC host qui doit rester 100% hors-ligne pendant la partie (voir CLAUDE.md).
  final displayFont = GoogleFonts.anton;
  final bodyTextTheme = GoogleFonts.spaceGroteskTextTheme(ThemeData(brightness: Brightness.dark).textTheme);

  TextStyle display(double size, {double letterSpacing = 0.5}) => displayFont(
        fontSize: size,
        fontWeight: FontWeight.w400,
        letterSpacing: letterSpacing,
        color: BlindifyColors.ink,
      );

  return ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    colorScheme: colorScheme,
    scaffoldBackgroundColor: BlindifyColors.bg,
    splashFactory: InkRipple.splashFactory,
    fontFamily: GoogleFonts.spaceGrotesk().fontFamily,
    appBarTheme: AppBarTheme(
      backgroundColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      centerTitle: false,
      titleTextStyle: display(20, letterSpacing: 0.8).copyWith(),
    ),
    cardTheme: CardThemeData(
      color: BlindifyColors.surfaceAlt,
      elevation: 0,
      shape: RoundedRectangleBorder(borderRadius: borderRadius, side: const BorderSide(color: BlindifyColors.ink, width: 2)),
      margin: EdgeInsets.zero,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: BlindifyColors.surfaceAlt,
      labelStyle: GoogleFonts.spaceMono(color: BlindifyColors.inkDim, fontSize: 12, letterSpacing: 0.4),
      hintStyle: const TextStyle(color: BlindifyColors.inkDim),
      border: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.ink, width: 2)),
      enabledBorder: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.ink, width: 2)),
      focusedBorder: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.cobalt, width: 3)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: BlindifyColors.cobalt,
        foregroundColor: BlindifyColors.onAccent,
        disabledBackgroundColor: BlindifyColors.surfaceAlt,
        disabledForegroundColor: BlindifyColors.inkDim,
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        shape: RoundedRectangleBorder(borderRadius: borderRadius, side: const BorderSide(color: BlindifyColors.ink, width: 2)),
        elevation: 0,
        textStyle: GoogleFonts.spaceGrotesk(fontWeight: FontWeight.w700, fontSize: 16, letterSpacing: 0.3),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: BlindifyColors.ink,
        side: const BorderSide(color: BlindifyColors.ink, width: 2),
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        shape: RoundedRectangleBorder(borderRadius: borderRadius),
      ),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: BlindifyColors.surfaceAlt,
      selectedColor: BlindifyColors.cobalt,
      side: const BorderSide(color: BlindifyColors.ink, width: 2),
      shape: const StadiumBorder(),
      labelStyle: GoogleFonts.spaceGrotesk(color: BlindifyColors.ink, fontWeight: FontWeight.w600),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
    ),
    dividerTheme: const DividerThemeData(color: BlindifyColors.borderSoft, space: 24),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: BlindifyColors.cobalt),
    textTheme: bodyTextTheme.copyWith(
      headlineSmall: display(26),
      titleLarge: display(22),
      titleMedium: display(18),
      titleSmall: GoogleFonts.spaceMono(
        fontWeight: FontWeight.w700,
        color: BlindifyColors.inkDim,
        fontSize: 12,
        letterSpacing: 1.2,
      ),
      bodyLarge: GoogleFonts.spaceGrotesk(color: BlindifyColors.ink),
      bodyMedium: GoogleFonts.spaceGrotesk(color: BlindifyColors.ink),
      bodySmall: GoogleFonts.spaceGrotesk(color: BlindifyColors.inkDim),
    ),
  );
}

/// Ombre dure décalée (pas de flou) — signature visuelle du style, cf `box-shadow: Npx Npx 0 couleur`
/// répété dans host/style.css sur `.screen`/`.cover-frame`/`.game-code`/boutons.
List<BoxShadow> hardShadow(Color color, {double offset = 6}) => [
      BoxShadow(color: color, blurRadius: 0, offset: Offset(offset, offset)),
    ];
