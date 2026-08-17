import 'package:flutter/material.dart';

/// Palette partagée avec host/style.css — même identité visuelle des deux côtés de la partie.
class BlindifyColors {
  BlindifyColors._();

  static const bg = Color(0xFF0D0E17);
  static const surface = Color(0xFF1A1C2B);
  static const surfaceAlt = Color(0xFF232640);
  static const text = Color(0xFFF3F4FC);
  static const textDim = Color(0xFF9A9DB8);
  static const accent = Color(0xFF7C8FFF);
  static const accent2 = Color(0xFFFF6EC7);
  static const good = Color(0xFF43E08A);
  static const bad = Color(0xFFFF5C72);
  static const warn = Color(0xFFFFB84D);
  static const border = Color(0xFF2E3150);
  static const onAccent = Color(0xFF0B0C14);
}

ThemeData buildBlindifyTheme() {
  final colorScheme = ColorScheme.fromSeed(
    seedColor: BlindifyColors.accent,
    brightness: Brightness.dark,
  ).copyWith(
    primary: BlindifyColors.accent,
    onPrimary: BlindifyColors.onAccent,
    secondary: BlindifyColors.accent2,
    onSecondary: BlindifyColors.onAccent,
    surface: BlindifyColors.surface,
    onSurface: BlindifyColors.text,
    error: BlindifyColors.bad,
    outline: BlindifyColors.border,
  );

  const radius = 14.0;
  final borderRadius = BorderRadius.circular(radius);

  return ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    colorScheme: colorScheme,
    scaffoldBackgroundColor: BlindifyColors.bg,
    splashFactory: InkRipple.splashFactory,
    appBarTheme: const AppBarTheme(
      backgroundColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(
        color: BlindifyColors.text,
        fontWeight: FontWeight.w800,
        fontSize: 20,
        letterSpacing: 0.2,
      ),
    ),
    cardTheme: CardThemeData(
      color: BlindifyColors.surfaceAlt,
      elevation: 0,
      shape: RoundedRectangleBorder(borderRadius: borderRadius, side: const BorderSide(color: BlindifyColors.border)),
      margin: EdgeInsets.zero,
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: BlindifyColors.surfaceAlt,
      labelStyle: const TextStyle(color: BlindifyColors.textDim),
      hintStyle: const TextStyle(color: BlindifyColors.textDim),
      border: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.border)),
      enabledBorder: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.border)),
      focusedBorder: OutlineInputBorder(borderRadius: borderRadius, borderSide: const BorderSide(color: BlindifyColors.accent, width: 2)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: BlindifyColors.accent,
        foregroundColor: BlindifyColors.onAccent,
        disabledBackgroundColor: BlindifyColors.surfaceAlt,
        disabledForegroundColor: BlindifyColors.textDim,
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        shape: RoundedRectangleBorder(borderRadius: borderRadius),
        textStyle: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        foregroundColor: BlindifyColors.text,
        side: const BorderSide(color: BlindifyColors.border),
        padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 16),
        shape: RoundedRectangleBorder(borderRadius: borderRadius),
      ),
    ),
    chipTheme: ChipThemeData(
      backgroundColor: BlindifyColors.surfaceAlt,
      selectedColor: BlindifyColors.accent,
      side: const BorderSide(color: BlindifyColors.border),
      shape: const StadiumBorder(),
      labelStyle: const TextStyle(color: BlindifyColors.text, fontWeight: FontWeight.w600),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
    ),
    dividerTheme: const DividerThemeData(color: BlindifyColors.border, space: 24),
    progressIndicatorTheme: const ProgressIndicatorThemeData(color: BlindifyColors.accent),
    textTheme: const TextTheme(
      headlineSmall: TextStyle(fontWeight: FontWeight.w800, color: BlindifyColors.text),
      titleLarge: TextStyle(fontWeight: FontWeight.w800, color: BlindifyColors.text),
      titleMedium: TextStyle(fontWeight: FontWeight.w700, color: BlindifyColors.text),
      titleSmall: TextStyle(fontWeight: FontWeight.w700, color: BlindifyColors.textDim, letterSpacing: 0.6),
      bodyLarge: TextStyle(color: BlindifyColors.text),
      bodyMedium: TextStyle(color: BlindifyColors.text),
      bodySmall: TextStyle(color: BlindifyColors.textDim),
    ),
  );
}
