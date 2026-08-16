using System.Text.Json;
using System.Text.Json.Serialization;
using Blindify.Api.Hubs;
using Blindify.Application.DependencyInjection;
using Blindify.Infrastructure.DependencyInjection;
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
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
});

// Réseau local uniquement (voir architecture.md section 2) : CORS ouvert aux clients LAN, pas de credentials.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Sert audio/ et covers/ sous /files — les chemins de tracks.json (ex. "audio/xxx.mp3") sont déjà
// relatifs à cette racine. Seul le host consomme ces fichiers, jamais les joueurs (voir CLAUDE.md).
var dataRootPath = Path.GetFullPath(app.Configuration["Data:RootPath"]!, app.Environment.ContentRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(dataRootPath),
    RequestPath = "/files"
});

app.UseCors();

app.MapGet("/", () => "Hello World!");
app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
