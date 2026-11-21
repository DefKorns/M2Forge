using System.Text.Json.Serialization;
using M2Forge.Core.Firmware;

namespace M2Forge.Core.Projects;

public sealed record TextureEdit(string Psb, string Texture, string? Icon, string Image);
