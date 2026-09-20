using Blindify.Domain.Entities;
using Blindify.Domain.Enums;
using Blindify.Domain.Jokers;

namespace Blindify.Application.Jokers;

public interface IJokerService
{
    /// <summary>Calcule l'indice de joker pour ce round (V2, section 12.7) — pur, jamais de jeton de
    /// pochette ici (voir JokerIndice.CoverToken, renseigné après coup par GameHub).</summary>
    JokerIndice CalculerIndice(RoundMode mode, RoundCible cible, Track track, List<RoundOption>? options, List<int>? anneeOptions);
}
