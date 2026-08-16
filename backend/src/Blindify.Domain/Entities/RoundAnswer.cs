namespace Blindify.Domain.Entities;

public class RoundAnswer
{
    public required string PlayerId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public required string Reponse { get; set; }
    public bool EstCorrecte { get; set; }
    public int Points { get; set; }
}
