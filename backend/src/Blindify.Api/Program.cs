using System.Security.Cryptography;
using System.Text;
using Blindify.Api.Contracts;
using Blindify.Api.Hubs;
using Blindify.Api.Jokers;
using Blindify.Application.DependencyInjection;
using Blindify.Infrastructure.DependencyInjection;
using Blindify.Infrastructure.Tracks;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RoundTimerCoordinator>();
builder.Services.AddSingleton<BonusTimerCoordinator>();
builder.Services.AddSingleton<IJokerCoverTokenStore, JokerCoverTokenStore>();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = ContractJsonOptions.Instance.PropertyNamingPolicy;
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
    foreach (var converter in ContractJsonOptions.Instance.Converters)
        options.PayloadSerializerOptions.Converters.Add(converter);
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
        // .apk absent du FileExtensionContentTypeProvider par défaut : sans ce mapping, le
        // middleware renvoie 404 plutôt que de servir un type inconnu (ServeUnknownFileTypes
        // reste false, comportement voulu partout ailleurs) — utilisé pour distribuer l'app
        // Flutter aux téléphones Android par le réseau local, sans câble (voir docs/architecture.md).
        var hostContentTypeProvider = new FileExtensionContentTypeProvider();
        hostContentTypeProvider.Mappings[".apk"] = "application/vnd.android.package-archive";
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = hostFileProvider, RequestPath = "" });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = hostFileProvider,
            RequestPath = "",
            ContentTypeProvider = hostContentTypeProvider,
            // Retour utilisateur : un git pull met host/ à jour sur le Pi, mais sans en-tête
            // Cache-Control explicite, le navigateur applique un cache heuristique (RFC 7234) sur ces
            // fichiers non versionnés dans l'URL — même fermer/rouvrir l'onglet ne suffit pas à voir
            // la nouvelle version, seul un rechargement forcé (Ctrl+Maj+R) contournait le cache.
            // no-cache (pas no-store) : le navigateur revalide toujours via ETag/Last-Modified — un
            // fichier inchangé reste servi en 304 (pas de re-téléchargement), un fichier modifié est
            // renvoyé immédiatement, sans que quiconque ait besoin de connaître l'astuce du rechargement forcé.
            OnPrepareResponse = ctx =>
                ctx.Context.Response.Headers.CacheControl = "no-cache, must-revalidate"
        });
    }
}

// Sert le build web de l'app joueur (flutter build web --base-href /play/) sous /play — pour les
// joueurs iPhone sans app native installée (pas de sideload .ipa sans Mac/Xcode, contrairement à
// l'.apk Android distribué via host/). Même pattern optionnel que Host:StaticPath ci-dessus : clé
// absente ou dossier introuvable = ignoré silencieusement. Le build n'est pas versionné avec Git
// (app/build/ est gitignored comme tout artefact de build) — déployé séparément sur le Pi, à
// reconstruire après chaque changement de app/lib.
var playStaticPath = app.Configuration["Play:StaticPath"];
if (!string.IsNullOrWhiteSpace(playStaticPath))
{
    var resolvedPlayStaticPath = Path.GetFullPath(playStaticPath, app.Environment.ContentRootPath);
    if (Directory.Exists(resolvedPlayStaticPath))
    {
        var playFileProvider = new PhysicalFileProvider(resolvedPlayStaticPath);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = playFileProvider, RequestPath = "/play" });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = playFileProvider, RequestPath = "/play" });
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

// V2, section 12.7 — pochette floutée pour l'indice de joker (TapeReponse + Titre/Auteur uniquement,
// voir GameHub.UtiliserJoker). Le jeton (opaque, IJokerCoverTokenStore) est la seule protection : jamais
// d'accès direct par trackId, sinon un joueur pourrait deviner la pochette d'un morceau pas encore joué.
// Flou volontairement fort (aucun texte/logo ne doit rester lisible) — dataRootPath déjà résolu plus haut
// pour /files, réutilisé tel quel (même convention de chemin que Track.CoverPath, ex. "covers/xxx.jpg").
app.MapGet("/api/joker/cover/{jeton}", (string jeton, IJokerCoverTokenStore tokenStore, ITracksRepository tracksRepository) =>
{
    var trackId = tokenStore.Resoudre(jeton);
    var track = trackId is null ? null : tracksRepository.GetById(trackId);
    if (track?.CoverPath is null) return Results.NotFound();

    var coverFullPath = Path.Combine(dataRootPath, track.CoverPath);
    if (!File.Exists(coverFullPath)) return Results.NotFound();

    using var image = Image.Load(coverFullPath);
    image.Mutate(x => x.GaussianBlur(40f));
    using var ms = new MemoryStream();
    image.SaveAsJpeg(ms);
    return Results.File(ms.ToArray(), "image/jpeg");
});

// Redémarrage à distance (retour utilisateur : pouvoir relancer le backend depuis la page host sans
// accès physique/SSH au Raspberry Pi). Protégé par un mot de passe (Admin:RestartPassword, jamais
// en dur — vide par défaut = fonctionnalité désactivée) : le réseau local n'est pas une frontière de
// confiance suffisante pour exposer un arrêt de process sans contrôle. N'arrête PAS le conteneur —
// se contente d'arrêter proprement le process ASP.NET Core ; c'est `restart: unless-stopped` côté
// docker-compose.yml qui relance le conteneur, voir docs/architecture.md section "Dockerisation".
app.MapPost("/api/admin/restart", (RestartRequestDto request, IConfiguration configuration, IHostApplicationLifetime lifetime) =>
{
    var motDePasseConfigure = configuration["Admin:RestartPassword"];
    if (string.IsNullOrEmpty(motDePasseConfigure))
        return Results.Problem("Redémarrage désactivé — aucun mot de passe configuré (Admin:RestartPassword).", statusCode: StatusCodes.Status503ServiceUnavailable);

    // Comparaison à temps constant : évite qu'un minutage de la réponse ne laisse deviner le mot de
    // passe caractère par caractère depuis le réseau local.
    var estValide = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(motDePasseConfigure), Encoding.UTF8.GetBytes(request.Password ?? ""));
    if (!estValide)
        return Results.Unauthorized();

    // Différé plutôt qu'appelé en synchrone ici : StopApplication() déclenche immédiatement la
    // dispose des services (logging compris) — appelé en synchrone, ça court-circuite l'envoi de
    // cette réponse HTTP elle-même (constaté avec TestServer : l'appelant ne reçoit jamais de 200).
    _ = Task.Delay(TimeSpan.FromMilliseconds(300)).ContinueWith(_ => lifetime.StopApplication());
    return Results.Ok(new { message = "Redémarrage en cours." });
});

app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
