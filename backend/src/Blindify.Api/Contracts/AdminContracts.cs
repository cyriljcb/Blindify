namespace Blindify.Api.Contracts;

/// <summary>Voir Program.cs — POST /api/admin/restart.</summary>
public record RestartRequestDto(string Password);

/// <summary>Voir GameHub.AuthenticateAdmin — mot de passe distinct de RestartPassword ci-dessus
/// (Admin:RemoteControlPassword), pensé pour être saisi dans l'app Flutter plutôt qu'au clavier du
/// Pi. ErrorMessage distingue "désactivé côté serveur" de "mot de passe incorrect" pour l'UI.</summary>
public record AdminAuthResultDto(bool Success, string? ErrorMessage);
