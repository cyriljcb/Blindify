/// Miroir de `Blindify.Domain.Enums.RoundMode` — sérialisé en string brute côté
/// backend (`JsonStringEnumConverter()` sans naming policy, voir Program.cs).
enum RoundMode { qcm, tapeReponse, premiereLettre }

extension RoundModeJson on RoundMode {
  static RoundMode fromJson(String value) {
    switch (value) {
      case 'Qcm':
        return RoundMode.qcm;
      case 'TapeReponse':
        return RoundMode.tapeReponse;
      case 'PremiereLettre':
        return RoundMode.premiereLettre;
    }
    throw ArgumentError('RoundMode inconnu reçu du serveur : $value');
  }

  String get label {
    switch (this) {
      case RoundMode.qcm:
        return 'QCM';
      case RoundMode.tapeReponse:
        return 'Réponse tapée';
      case RoundMode.premiereLettre:
        return 'Première lettre';
    }
  }
}
