using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;
using Blindify.Domain.Enums;

namespace Blindify.Application.Qcm;

/// <summary>Génération des QCM (3 distracteurs + piège éventuel) — voir architecture.md section 6.</summary>
public interface IQcmGenerator
{
    /// <summary>
    /// GameConfig.NombreDistracteursQcm distracteurs (3 par défaut) tirés du pool genre/tag, avec une
    /// chance (ProbabiliteQcmPiege) qu'un distracteur soit un piège (trapWith) plutôt qu'aléatoire.
    /// Jamais deux options au même libellé affiché pour <paramref name="cible"/> — si le pool ne le
    /// permet pas, moins d'options plutôt qu'un doublon.
    /// </summary>
    QcmOptions GenererOptions(Track correct, IReadOnlyList<Track> pool, GameConfig config, Random random, RoundCible cible = RoundCible.Titre);
}
