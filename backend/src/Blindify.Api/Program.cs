using System.Text.Json;
using Blindify.Api.Hubs;
using Blindify.Application.DependencyInjection;
using Blindify.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<RoundTimerCoordinator>();

builder.Services.AddSignalR().AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

app.MapGet("/", () => "Hello World!");
app.MapHub<GameHub>("/hubs/game");

app.Run();

public partial class Program;
