namespace Blindify.Api.Contracts;

/// <summary>Voir Program.cs — GET /api/ping. Service vaut toujours "blindify" : c'est ce que l'app
/// vérifie pendant le scan du sous-réseau, pour ne pas prendre pour le Pi n'importe quel autre
/// appareil du réseau qui répondrait en HTTP sur le port 5000.</summary>
public record PingDto(string Service);
