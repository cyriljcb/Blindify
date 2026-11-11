using Blindify.Api.Contracts;
using Blindify.Application.Rounds;
using Blindify.Domain.Entities;
using Blindify.Infrastructure.Tracks;

namespace Blindify.Api.Hubs;

/// <summary>Construit les DTO de révélation (RoundEnded/BonusResult) — partagé entre les
/// coordinateurs de chrono (diffusion en direct) et GameHub.RejoinAsHost (reprise du host après un
/// rechargement de la page, audit 2026-11-10). Appelé sous session.Lock.</summary>
internal static class RevealDtoBuilder
{
    public static RoundEndedDto RoundEnded(GameSession session, Round round, ITracksRepository tracksRepository)
    {
        var track = tracksRepository.GetById(round.TrackId);
        var resultats = session.Players
            .Select(p =>
            {
                var reponse = round.Reponses.FirstOrDefault(r => r.PlayerId == p.PlayerId);
                return new RoundResultEntryDto(p.PlayerId, reponse?.Reponse, reponse?.EstCorrecte, reponse?.Points ?? 0, reponse?.EcartAnnee);
            })
            .ToList();
        var film = track is not null ? FilmNameResolver.Resoudre(track) : "?";
        var bonneOption = round.Options?.FirstOrDefault(o => o.TrackId == round.TrackId)?.OptionId;

        return new RoundEndedDto(round.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, round.Cible, film, resultats, track?.Year, bonneOption);
    }

    public static BonusResultDto BonusResult(BonusRound bonusRound, ITracksRepository tracksRepository)
    {
        var track = tracksRepository.GetById(bonusRound.TrackId);
        var resultats = bonusRound.Reponses
            .Select(r => new BonusResultEntryDto(r.PlayerId, Math.Abs(r.Points), r.Reponse, r.EstCorrecte, r.Points, r.EcartAnnee))
            .ToList();
        var film = track is not null ? FilmNameResolver.Resoudre(track) : "?";
        var bonneOption = bonusRound.Options?.FirstOrDefault(o => o.TrackId == bonusRound.TrackId)?.OptionId;

        return new BonusResultDto(bonusRound.TrackId, track?.Title ?? "?", track?.Artist ?? "?", track?.CoverPath, bonusRound.Cible, film, resultats, bonusRound.EstCourse, track?.Year, bonneOption);
    }
}
