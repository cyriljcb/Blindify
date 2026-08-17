import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'screens/bonus_question_screen.dart';
import 'screens/bonus_result_screen.dart';
import 'screens/bonus_stake_screen.dart';
import 'screens/connect_screen.dart';
import 'screens/ended_screen.dart';
import 'screens/join_screen.dart';
import 'screens/leaderboard_overlay.dart';
import 'screens/lobby_screen.dart';
import 'screens/round_ended_screen.dart';
import 'screens/round_screen.dart';
import 'services/game_connection.dart';
import 'theme.dart';

void main() {
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
      AppScreen.connect => const ConnectScreen(),
      AppScreen.join => const JoinScreen(),
      AppScreen.lobby => const LobbyScreen(),
      AppScreen.round => const RoundScreen(),
      AppScreen.roundEnded => const RoundEndedScreen(),
      AppScreen.bonusStake => const BonusStakeScreen(),
      AppScreen.bonusQuestion => const BonusQuestionScreen(),
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
            colors: [Color(0x297C8FFF), BlindifyColors.bg],
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
                    ShaderMask(
                      shaderCallback: (bounds) => const LinearGradient(
                        colors: [BlindifyColors.accent, BlindifyColors.accent2],
                      ).createShader(bounds),
                      child: const Text(
                        'Blindify',
                        style: TextStyle(fontWeight: FontWeight.w900, fontSize: 22, color: Colors.white),
                      ),
                    ),
                    const Spacer(),
                    _ConnectionPill(connected: game.connected),
                  ],
                ),
              ),
              Expanded(
                child: Stack(
                  children: [
                    Padding(
                      padding: const EdgeInsets.all(16),
                      child: AnimatedSwitcher(
                        duration: const Duration(milliseconds: 320),
                        switchInCurve: Curves.easeOut,
                        switchOutCurve: Curves.easeIn,
                        transitionBuilder: (child, animation) =>
                            FadeTransition(opacity: animation, child: child),
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
    final color = connected ? BlindifyColors.good : BlindifyColors.textDim;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.15),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(width: 8, height: 8, decoration: BoxDecoration(color: color, shape: BoxShape.circle)),
          const SizedBox(width: 6),
          Text(
            connected ? 'connecté' : 'déconnecté',
            style: TextStyle(color: color, fontWeight: FontWeight.w700, fontSize: 12),
          ),
        ],
      ),
    );
  }
}
