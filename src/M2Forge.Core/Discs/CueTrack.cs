using System.Text.RegularExpressions;

namespace M2Forge.Core.Discs;

public sealed record CueTrack(int Number, bool IsAudio, string File, int Pregap, int? Index00, int Index01);
