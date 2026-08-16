namespace Blindify.Application.Qcm;

public record QcmOptions(string CorrectTrackId, IReadOnlyList<string> OptionsTrackIds);
