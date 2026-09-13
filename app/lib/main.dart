import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_animate/flutter_animate.dart';
import 'package:google_fonts/google_fonts.dart';
import 'package:provider/provider.dart';

import 'motion.dart';
import 'screens/answer_phase_screen.dart';
import 'screens/bonus_course_intro_screen.dart';
import 'screens/bonus_result_screen.dart';
import 'screens/bonus_stake_screen.dart';
import 'screens/connect_screen.dart';
import 'screens/ended_screen.dart';
import 'screens/join_screen.dart';
import 'screens/leaderboard_overlay.dart';
import 'screens/loading_screen.dart';
import 'screens/lobby_screen.dart';
import 'screens/round_ended_screen.dart';
import 'screens/serie_intro_screen.dart';
import 'services/game_connection.dart';
import 'theme.dart';
import 'widgets/settings_sheet.dart';

void main() async {
  // Buzzer tenu à deux mains en mode portrait — un paysage accidentel casserait la mise en page
  // (grille de lettres, QCM) pensée pour ce format. Ignoré sur desktop/web (no-op silencieux si
  // la plateforme ne supporte pas la contrainte, par ex. Flutter web sur certains navigateurs).
  WidgetsFlutterBinding.ensureInitialized();
  await SystemChrome.setPreferredOrientations([
    DeviceOrientation.portraitUp,
    DeviceOrientation.portraitDown,
  ]);
  runApp(const BlindifyApp());
}

class BlindifyApp extends StatelessWidget {
  const BlindifyApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ChangeNotifierProvider(
      create: (_) => GameConnection()..init(),
      child: MaterialApp(
        title: 'Blindify',
        debugShowCheckedModeBanner: false,
        theme: buildBlindifyTheme(),
        home: const _RootScreen(),
      ),
    );
  }
}

class _RootScreen extends StatelessWidget {
  const _RootScreen();

  @override
  Widget build(BuildContext context) {
    final game = context.watch<GameConnection>();

    final Widget body = switch (game.screen) {
      AppScreen.loading => const LoadingScreen(),
      AppScreen.connect => const ConnectScreen(),
      AppScreen.join => const JoinScreen(),
      AppScreen.lobby => const LobbyScreen(),
      AppScreen.serieIntro => const SerieIntroScreen(),
      AppScreen.round => const AnswerPhaseScreen(variant: AnswerPhaseVariant.classique),
      AppScreen.roundEnded => const RoundEndedScreen(),
      AppScreen.bonusStake => const BonusStakeScreen(),
      AppScreen.bonusCourseIntro => const BonusCourseIntroScreen(),
      AppScreen.bonusQuestion => const AnswerPhaseScreen(variant: AnswerPhaseVariant.bonus),
      AppScreen.bonusResult => const BonusResultScreen(),
      AppScreen.ended => const EndedScreen(),
    };

    return Scaffold(
      extendBodyBehindAppBar: false,
      body: Container(
        decoration: const BoxDecoration(
          gradient: RadialGradient(
            center: Alignment(0, -0.9),
            radius: 1.4,
            colors: [Color(0x293D5AFF), BlindifyColors.bg],
            stops: [0, 0.6],
          ),
        ),
        child: SafeArea(
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 12, 20, 4),
                child: Row(
                  children: [
                    Text(
                      'BLINDIFY',
                      style: TextStyle(
                        fontFamily: GoogleFonts.anton().fontFamily,
                        fontWeight: FontWeight.w400,
                        fontSize: 24,
                        letterSpacing: 0.5,
                        color: BlindifyColors.ink,
                        // Ombre dure décalée (pas de flou) — même motif que header h1 côté host.
                        shadows: const [Shadow(color: BlindifyColors.coral, offset: Offset(3, 3), blurRadius: 0)],
                      ),
                    )
                        .animate()
                        .fadeIn(duration: BlindifyMotion.normal)
                        .slideX(begin: -0.2, curve: BlindifyMotion.pop),
                    const Spacer(),
                    _ConnectionPill(connected: game.connected),
                    const SizedBox(width: 8),
                    IconButton(
                      onPressed: () => showSettingsSheet(context),
                      icon: const Icon(Icons.settings_rounded, color: BlindifyColors.ink),
                      tooltip: 'Réglages',
                    ),
                  ],
                ),
              ),
              Expanded(
                child: Stack(
                  children: [
                    Padding(
                      padding: const EdgeInsets.all(16),
                      // Fade + léger zoom-in + glissement vertical (au lieu d'un simple fondu) — retour
                      // utilisateur : l'app manquait de dynamisme, chaque écran doit "arriver" plutôt
                      // que de simplement apparaître. Le nouvel écran survient toujours par-dessus
                      // l'ancien (Stack, pas de croisement des deux transitions) pour éviter un flash
                      // de contenu à moitié sorti derrière celui qui arrive.
                      child: AnimatedSwitcher(
                        duration: BlindifyMotion.normal,
                        switchInCurve: BlindifyMotion.pop,
                        switchOutCurve: Curves.easeIn,
                        layoutBuilder: (currentChild, previousChildren) => Stack(
                          alignment: Alignment.topCenter,
                          children: [...previousChildren, ?currentChild],
                        ),
                        transitionBuilder: (child, animation) => FadeTransition(
                          opacity: animation,
                          child: ScaleTransition(
                            scale: Tween(begin: 0.94, end: 1.0).animate(animation),
                            child: SlideTransition(
                              position: Tween(begin: const Offset(0, 0.04), end: Offset.zero).animate(animation),
                              child: child,
                            ),
                          ),
                        ),
                        child: KeyedSubtree(key: ValueKey(game.screen), child: body),
                      ),
                    ),
                    if (game.showLeaderboard) const LeaderboardOverlay(),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ConnectionPill extends StatelessWidget {
  const _ConnectionPill({required this.connected});

  final bool connected;

  @override
  Widget build(BuildContext context) {
    final bg = connected ? BlindifyColors.good : BlindifyColors.surfaceAlt;
    final fg = connected ? BlindifyColors.onLight : BlindifyColors.inkDim;
    final border = connected ? BlindifyColors.ink : BlindifyColors.borderSoft;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: bg,
        border: Border.all(color: border, width: 2),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        connected ? 'connecté' : 'déconnecté',
        style: GoogleFonts.spaceMono(
          color: fg,
          fontWeight: FontWeight.w700,
          fontSize: 11,
          letterSpacing: 0.6,
        ),
      ),
    );
  }
}
