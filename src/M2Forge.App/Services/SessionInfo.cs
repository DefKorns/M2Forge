using System.IO;
using System.Text;
using System.Text.Json;
using M2Forge.App.ViewModels;

namespace M2Forge.App.Services;

public sealed record SessionInfo(string Source, bool ConsoleDetected, int Page);
