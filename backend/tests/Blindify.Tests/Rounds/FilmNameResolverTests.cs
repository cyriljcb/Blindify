using Blindify.Application.Rounds;
using Blindify.Domain.Entities;

namespace Blindify.Tests.Rounds;

public class FilmNameResolverTests
{
    private static Track NouveauTrack(string title, string? album) => new()
    {
        Id = "a",
        Title = title,
        Artist = "Artiste",
        Album = album,
        FilePath = "audio/a.mp3"
    };

    [Theory]
    // ----- Format anglais (D23) -----
    [InlineData("If I Didn't Have You", "Monsters, Inc. (Original Motion Picture Soundtrack)", "Monsters, Inc.")]
    [InlineData("A Whole New World", "Aladdin (Original Motion Picture Soundtrack)", "Aladdin")]
    [InlineData("Friend Like Me", "Aladdin Special Edition", "Aladdin")]
    [InlineData("Bibbidi-Bobbidi-Boo", "Cinderella Special Edition (Original Motion Picture Soundtrack/Japanese Version)", "Cinderella")]
    [InlineData("Let It Go", "Frozen (Original Motion Picture Soundtrack / Deluxe Edition)", "Frozen")]
    [InlineData("Beauty and the Beast", "Beauty and the Beast", "Beauty and the Beast")]
    [InlineData("I Knew It, I Knew You", "I Knew It, I Knew You (From \"Toy Story 5\")", "Toy Story 5")]
    // ----- Format français (playlist VF) -----
    [InlineData("Libérée, Délivrée", "La Reine des Neiges (Bande Originale Française du Film)", "La Reine des Neiges")]
    [InlineData("Ce Rêve Bleu", "Aladdin Original Soundtrack (French Version)", "Aladdin")]
    [InlineData("Le Festin", "Ratatouille Original Soundtrack (International Version)", "Ratatouille")]
    [InlineData("Un Poco Loco", "Coco (Bande Originale Française du Film)", "Coco")]
    [InlineData("Enfin Je Brille", "Raiponce OST", "Raiponce")]
    [InlineData("Un Air De Fête", "Le Roi Lion (Deluxe Collection - Lion King)", "Le Roi Lion")]
    [InlineData("Chanson", "The Lion King: Special Edition Original Soundtrack (French Version)", "The Lion King")]
    [InlineData("Chanson", "La Princesse Et La Grenouille (The Princess & The Frog)", "La Princesse Et La Grenouille")]
    [InlineData("Clochette", "Clochette Et La Pierre De Lune", "Clochette Et La Pierre De Lune")]
    public void Resoudre_NettoieLAlbumSelonLeFormatConnu(string titre, string? album, string filmAttendu)
    {
        var track = NouveauTrack(titre, album);

        Assert.Equal(filmAttendu, FilmNameResolver.Resoudre(track));
    }

    [Theory]
    [InlineData("Il vit en toi - Extrait de \"Le roi lion 2\"", "Cerise chante Disney", "Le roi lion 2")]
    [InlineData("Jamais je n'avouerai - Extrait de \"Hercule\"", "Cerise chante Disney", "Hercule")]
    [InlineData("Chem-chem Cheminée - De \"Mary Poppins\"", "Disney: Les 50 Plus Belles Chansons (3 Vol.)", "Mary Poppins")]
    public void Resoudre_PrivilegieLeFilmMentionneDansLeTitre_MemeSurAlbumDeCompilation(string titre, string? album, string filmAttendu)
    {
        var track = NouveauTrack(titre, album);

        Assert.Equal(filmAttendu, FilmNameResolver.Resoudre(track));
    }

    [Fact]
    public void Resoudre_AlbumDeCompilationSansIndiceDeFilm_RetombeSurLAlbumTelQuel()
    {
        // Best-effort documenté : un album "greatest hits" qui ne nomme aucun film et dont le
        // titre ne le mentionne pas non plus ne peut pas être résolu par nettoyage de texte —
        // nécessite une correction manuelle de tracks.json au cas par cas.
        var track = NouveauTrack("Sous l'océan", "La Magie De Disney");

        Assert.Equal("La Magie De Disney", FilmNameResolver.Resoudre(track));
    }

    [Fact]
    public void Resoudre_AlbumAbsent_RetombeSurLeTitre()
    {
        var track = NouveauTrack("Titre Seul", null);

        Assert.Equal("Titre Seul", FilmNameResolver.Resoudre(track));
    }
}
