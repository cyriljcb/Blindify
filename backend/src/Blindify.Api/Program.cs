using System.Text.Json;
using System.Text.Json.Serialization;
using Blindify.Api.Hubs;
using Blindify.Application.DependencyInjection;
using Blindify.Infrastructure.DependencyInjection;
using Blindify.Infrastructure.Tracks;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RoundTimerCoordinator>();
builder.Services.AddSingleton<BonusTimerCoordinator>();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Réseau local uniquement (voir architecture.md section 2) : CORS ouvert aux clients LAN, pas de credentials.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// UseCors doit précéder UseStaticFiles : sinon les fichiers statiques répondent avant que les en-têtes
// CORS ne soient ajoutés, et le navigateur bloque les requêtes cross-origin vers /files/*.
app.UseCors();

// Sert audio/ et covers/ sous /files — les chemins de tracks.json (ex. "audio/xxx.mp3") sont déjà
// relatifs à cette racine. Seul le host consomme ces fichiers, jamais les joueurs (voir CLAUDE.md).
var dataRootPath = Path.GetFullPath(app.Configuration["Data:RootPath"]!, app.Environment.ContentRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dataRootPath),
    RequestPath = "/files"
});

// Sert host/ (index.html, display.html, app.js...) à la racine — retour utilisateur du 2026-08-24 :
// évite d'avoir à recopier/servir ce dossier séparément sur le PC du host, un simple navigateur
// pointé sur l'IP du Pi suffit. Reste de simples fichiers statiques, aucune étape de build : ça ne
// "dockerise" pas le frontend au sens de CLAUDE.md, ça les sert au même titre que /files ci-dessus.
// Optionnel (clé absente ou dossier introuvable = ignoré silencieusement) pour ne rien casser côté
// tests d'intégration (WebApplicationFactory, aucune config Host:StaticPath fournie) ni pour qui
// lance juste l'API sans avoir le dossier host/ sous la main.
var hostStaticPath = app.Configuration["Host:StaticPath"];
if (!string.IsNullOrWhiteSpace(hostStaticPath))
{
    var resolvedHostStaticPath = Path.GetFullPath(hostStaticPath, app.Environment.ContentRootPath);
    if (Directory.Exists(resolvedHostStaticPath))
    {
        var hostFileProvider = new PhysicalFileProvider(resolvedHostStaticPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = hostFileProvider, RequestPath = "" });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = hostFileProvider, RequestPath = "" });
    }
}

// Alimente le sélecteur de thèmes du panneau de contrôle (cases à cocher, voir host/app.js) —
// tags manuels/semi-automatiques de tracks.json (architecture.md section 4), pas les genres
// Spotify (trop nombreux/bruités pour un choix de thème joueur).
app.MapGet("/api/tags", (ITracksRepository tracksRepository) =>
    tracksRepository.GetAll()
        .SelectMany(t => t.Tags)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
        .ToList());

app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
