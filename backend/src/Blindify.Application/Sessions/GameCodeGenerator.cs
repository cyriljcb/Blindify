namespace Blindify.Application.Sessions;

public class GameCodeGenerator : IGameCodeGenerator
{
    // Sans caractères ambigus à l'oral/à l'écrit (pas de 0/O, 1/I).
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Longueur = 5;

    public string GenererCode()
    {
        Span<char> chars = stackalloc char[Longueur];
        for (var i = 0; i < Longueur; i++)
            chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];

        return new string(chars);
    }
}
