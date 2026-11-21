using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using M2Forge.App.Services;
using M2Forge.Core.Firmware;

namespace M2Forge.App.ViewModels;

public sealed record FirmwareCheck(string Label, bool Ok, string Detail);
