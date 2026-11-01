using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Blindify.Api.Logging;

/// <summary>Écrit les logs dans un fichier par jour (<c>blindify-AAAA-MM-JJ.log</c>) sous
/// <c>Logs:Path</c> — retour utilisateur (2026-11-01) : les logs Docker disparaissent à chaque
/// rebuild du conteneur (<c>docker compose up -d --build</c>), et avec eux la trace des problèmes
/// rencontrés en partie (joueurs "incapables de répondre"). Le dossier est monté en volume sur le
/// Pi (docker-compose.yml), à côté des autres données. Pas de dépendance externe (Serilog...) : un
/// fichier texte par jour suffit, purgé au-delà de <c>Logs:RetentionDays</c>.
///
/// Écriture sur une tâche de fond (file d'attente bornée) : un log ne bloque jamais le hub
/// SignalR sur une écriture disque. Les niveaux suivent la section "Logging" habituelle
/// d'appsettings.json, comme pour la console.</summary>
public sealed class FichierLoggerProvider : ILoggerProvider
{
    private readonly string _dossier;
    private readonly int _retentionJours;
    private readonly Channel<string> _file = Channel.CreateBounded<string>(new BoundedChannelOptions(10_000)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });
    private readonly ConcurrentDictionary<string, FichierLogger> _loggers = new();
    private readonly Task _ecriture;

    public FichierLoggerProvider(string dossier, int retentionJours)
    {
        _dossier = dossier;
        _retentionJours = retentionJours;
        Directory.CreateDirectory(_dossier);
        _ecriture = Task.Run(EcrireEnContinuAsync);
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, nom => new FichierLogger(nom, _file.Writer));

    private async Task EcrireEnContinuAsync()
    {
        DateOnly? jourCourant = null;
        StreamWriter? writer = null;
        try
        {
            await foreach (var ligne in _file.Reader.ReadAllAsync())
            {
                var jour = DateOnly.FromDateTime(DateTime.Now);
                if (jour != jourCourant)
                {
                    writer?.Dispose();
                    writer = OuvrirFichierDuJour(jour);
                    jourCourant = jour;
                    PurgerAnciensFichiers();
                }

                await writer!.WriteAsync(ligne);
                // Vide le tampon dès que la file est vide : un crash du process ne perd alors au pire
                // que les lignes encore en attente, pas tout un tampon de 4 Ko.
                if (_file.Reader.Count == 0) await writer.FlushAsync();
            }
        }
        catch (Exception)
        {
            // Disque plein, dossier démonté... : les logs fichier s'arrêtent, jamais le backend.
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private StreamWriter OuvrirFichierDuJour(DateOnly jour)
    {
        var chemin = Path.Combine(_dossier, $"blindify-{jour:yyyy-MM-dd}.log");
        var flux = new FileStream(chemin, FileMode.Append, FileAccess.Write, FileShare.Read);
        return new StreamWriter(flux, new UTF8Encoding(false));
    }

    private void PurgerAnciensFichiers()
    {
        var limite = DateOnly.FromDateTime(DateTime.Now).AddDays(-_retentionJours);
        foreach (var fichier in Directory.EnumerateFiles(_dossier, "blindify-*.log"))
        {
            var date = Path.GetFileNameWithoutExtension(fichier)["blindify-".Length..];
            if (DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var jour) && jour < limite)
            {
                try { File.Delete(fichier); } catch (IOException) { }
            }
        }
    }

    public void Dispose()
    {
        _file.Writer.TryComplete();
        _ecriture.Wait(TimeSpan.FromSeconds(2));
    }

    private sealed class FichierLogger(string categorie, ChannelWriter<string> file) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var ligne = new StringBuilder()
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                .Append(' ').Append(Niveau(logLevel))
                .Append(' ').Append(categorie)
                .Append(" — ").Append(formatter(state, exception));
            if (exception is not null) ligne.AppendLine().Append(exception);
            ligne.AppendLine();

            file.TryWrite(ligne.ToString());
        }

        private static string Niveau(LogLevel niveau) => niveau switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "FAIL",
            LogLevel.Critical => "CRIT",
            _ => "????",
        };
    }
}
