/// Miroir de `JokerIndiceDto` (V2, section 12.7) — un seul type pour les 5 combinaisons mode/cible
/// possibles, chacun ne renseignant que le(s) champ(s) qui le concernent.
class JokerIndice {
  JokerIndice({this.optionsRetirees, this.tuilesRestantes, this.structure, this.decennie, this.coverUrl});

  /// TrackId (Qcm normal) ou années en texte (Qcm + cible Annee) — exactement les valeurs déjà
  /// passées à submitAnswer, à exclure de l'affichage.
  final List<String>? optionsRetirees;

  /// PremiereLettre — les lettres restant sélectionnables (dont la bonne).
  final List<String>? tuilesRestantes;

  /// TapeReponse, Titre/Auteur/Film — texte masqué lettre par lettre.
  final String? structure;

  /// TapeReponse, Annee — décennie de sortie (ex. 1980).
  final int? decennie;

  /// Chemin relatif vers la pochette floutée (à préfixer par serverBaseUrl) — TapeReponse + Titre/
  /// Auteur uniquement.
  final String? coverUrl;

  factory JokerIndice.fromJson(Map<String, dynamic> json) => JokerIndice(
        optionsRetirees: (json['optionsRetirees'] as List<dynamic>?)?.cast<String>(),
        tuilesRestantes: (json['tuilesRestantes'] as List<dynamic>?)?.cast<String>(),
        structure: json['structure'] as String?,
        decennie: json['decennie'] as int?,
        coverUrl: json['coverUrl'] as String?,
      );
}
