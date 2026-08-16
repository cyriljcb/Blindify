namespace Blindify.Domain.Configuration;

/// <summary>
/// Paramètres instanciés par série (jamais de constantes en dur) — voir architecture.md section 11.
/// </summary>
public class SeriesConfig
{
    public int NombreRoundsClassiques { get; set; }
    public int DureeFenetreReponseMs { get; set; }

    /// <summary>4 paliers croissants (safe / moyen / moyen+ / risqué) — voir architecture.md section 7.</summary>
    public int[] PaliersDeMise { get; set; } = new int[4];

    public int DureePhaseMiseMs { get; set; }
    public int DureePhaseQuestionMs { get; set; }
}
