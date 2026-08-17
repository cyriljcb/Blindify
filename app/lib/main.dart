import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'screens/connect_screen.dart';
import 'screens/ended_screen.dart';
import 'screens/join_screen.dart';
import 'screens/leaderboard_overlay.dart';
import 'screens/lobby_screen.dart';
import 'screens/round_ended_screen.dart';
import 'screens/round_screen.dart';
import 'services/game_connection.dart';

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
        theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: Colors.deepPurple), useMaterial3: true),
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
      AppScreen.ended => const EndedScreen(),
    };

    return Scaffold(
      appBar: AppBar(
        title: const Text('Blindify'),
        actions: [
          Padding(
            padding: const EdgeInsets.only(right: 16),
            child: Center(
              child: Chip(
                label: Text(game.connected ? 'connecté' : 'déconnecté'),
                backgroundColor: game.connected ? Colors.green.shade100 : Colors.red.shade100,
              ),
            ),
          ),
        ],
      ),
      body: Stack(
        children: [
          body,
          if (game.showLeaderboard) const LeaderboardOverlay(),
        ],
      ),
    );
  }
}
