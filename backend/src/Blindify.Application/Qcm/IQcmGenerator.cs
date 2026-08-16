using Blindify.Domain.Configuration;
using Blindify.Domain.Entities;

namespace Blindify.Application.Qcm;

/// <summary>Génération des QCM (3 distracteurs + piège éventuel) — voir architecture.md section 6.</summary>
public interface IQcmGenerator
{
    /// <summary>
    /// 3 distracteurs tirés du pool genre/tag par défaut, avec une chance (ProbabiliteQcmPiege) qu'un
    /// distracteur soit un piège (trapWith) plutôt qu'aléatoire. Si le pool genre/tag est insuffisant,
    /// complète avec le pool global — garantit toujours 4 options valides.
    /// </summary>
    QcmOptions GenererOptions(Track correct, IReadOnlyList<Track> pool, GameConfig config, Random random);
}
