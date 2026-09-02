using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blindify.Api.Contracts;

/// <summary>Options JSON du contrat SignalR (camelCase + enums en string) — partagées entre
/// Program.cs (protocole réel) et les tests (GameHubTestFactory, ContractsSerializationTests) pour
/// n'avoir qu'un seul endroit à faire évoluer si le format change.</summary>
public static class ContractJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
