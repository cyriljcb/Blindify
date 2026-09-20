/// Miroir de `Blindify.Domain.Enums.RaisonSignalement` (V2, section 12.4) — voir GameHub.SignalerMorceau.
enum RaisonSignalement {
  pasSaPlace,
  mauvaiseVersion,
  audioDefectueux,
  metadonneesFausses,
  horsTheme,
  refrainMalPlace,
  autre;

  /// Valeur envoyée au serveur — doit correspondre exactement au nom C# de l'enum (sérialisé en
  /// string côté contrat SignalR, voir ContractJsonOptions).
  String get code => switch (this) {
        RaisonSignalement.pasSaPlace => 'PasSaPlace',
        RaisonSignalement.mauvaiseVersion => 'MauvaiseVersion',
        RaisonSignalement.audioDefectueux => 'AudioDefectueux',
        RaisonSignalement.metadonneesFausses => 'MetadonneesFausses',
        RaisonSignalement.horsTheme => 'HorsTheme',
        RaisonSignalement.refrainMalPlace => 'RefrainMalPlace',
        RaisonSignalement.autre => 'Autre',
      };

  String get libelle => switch (this) {
        RaisonSignalement.pasSaPlace => "N'a rien à faire dans le catalogue",
        RaisonSignalement.mauvaiseVersion => 'Mauvaise version (live, remix, reprise...)',
        RaisonSignalement.audioDefectueux => 'Audio défectueux (coupure, volume, qualité...)',
        RaisonSignalement.metadonneesFausses => 'Métadonnées fausses (titre, artiste, année...)',
        RaisonSignalement.horsTheme => 'Hors thème de la série',
        RaisonSignalement.refrainMalPlace => 'Refrain mal placé',
        RaisonSignalement.autre => 'Autre',
      };

  /// Un commentaire est exigé côté UI pour cette raison — voir docs/architecture.md section 12.4.
  bool get commentaireObligatoire => this == RaisonSignalement.autre;
}
