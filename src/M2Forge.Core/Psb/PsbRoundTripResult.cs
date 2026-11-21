using GMWare.M2.Psb;

namespace M2Forge.Core.Psb;

public sealed record PsbRoundTripResult(
    string Path,
    ushort Version,
    PsbFlags Flags,
    bool BytesIdentical,
    bool TreeEqual,
    bool StreamsEqual,
    int OriginalLength,
    int RewrittenLength)
{
    public bool Equivalent => TreeEqual && StreamsEqual;
}
