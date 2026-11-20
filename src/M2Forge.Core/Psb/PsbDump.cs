using GMWare.M2.Psb;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace M2Forge.Core.Psb;

public static class PsbDump
{
    public static string ToJson(string psbPath)
    {
        using var file = File.OpenRead(psbPath);
        using var reader = new PsbReader(file, null, null);
        return ToJson(reader.Root);
    }

    public static string ToJson(JToken root)
    {
        var text = new StringWriter();
        using (var json = new JsonTextWriter(text) { Formatting = Formatting.Indented })
            root.WriteTo(json);
        return text.ToString();
    }

    public static bool SameText(string a, string b) =>
        a.Replace("\r\n", "\n").TrimEnd() == b.Replace("\r\n", "\n").TrimEnd();
}
