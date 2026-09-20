using Blindify.Domain.Enums;
using Blindify.Domain.Jokers;

namespace Blindify.Domain.Entities;

/// <summary>Round classique — voir architecture.md section 6.</summary>
public class Round
{
    /// <summary>Identité stable de CE round (V2, reconnexion) — envoyée aux clients dans RoundStarted et
    /// reprise dans SubmitAnswer pour que le serveur ignore silencieusement une soumission tardive
    /// arrivée après que le round suivant a déjà démarré (voir GameHub.SubmitAnswer).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string TrackId { get; set; }
    public RoundMode Mode { get; set; }

    /// <summary>Titre ou Auteur — tiré aléatoirement au démarrage (RoundService.DemarrerRound),
    /// utilisé aussi bien pour la validation des réponses (TapeReponse/PremiereLettre) que pour
    /// indiquer au joueur ce qui est demandé.</summary>
    public RoundCible Cible { get; set; }

    public DateTimeOffset? DebutRound { get; set; }
    public long DureeEnPauseMs { get; set; }
    public List<RoundAnswer> Reponses { get; set; } = [];

    /// <summary>Les 4 IDs de morceaux proposés (mode Qcm uniquement), générés au démarrage du round.</summary>
    public List<string>? QcmOptionTrackIds { get; set; }

    /// <summary>Les options QCM réellement présentées (mode Qcm uniquement, V2) — voir RoundOption.
    /// Construit une seule fois au démarrage du round (GameHub.StartRound), jamais recalculé
    /// (contrairement à QcmOptionTrackIds qui ne porte que les IDs) : la reconnexion et le calcul des
    /// statistiques de réponse s'appuient dessus plutôt que de reconstruire les feintes à la volée.</summary>
    public List<RoundOption>? Options { get; set; }

    /// <summary>Les 4 années proposées (cible Année + mode Qcm uniquement, V2 section 12.5), triées,
    /// générées au démarrage du round — voir AnneeQcmGenerator. Distinct de Options : ce ne sont pas
    /// des morceaux (pas de TrackId/feinte/piège), juste des années.</summary>
    public List<int>? AnneeOptions { get; set; }

    /// <summary>Indice déjà obtenu par joueur ayant utilisé son joker SUR CE ROUND (V2, section 12.7) —
    /// borné à la durée de vie de cet objet Round (un nouveau round = un nouvel objet, jamais besoin de
    /// nettoyer explicitement). Permet à ConstruireEtatCourantJoueur de rejouer le même indice (pas un
    /// nouveau tirage) si le joueur se reconnecte pendant ce round.</summary>
    public Dictionary<string, JokerIndice> JokerIndicesParJoueur { get; set; } = [];
}
