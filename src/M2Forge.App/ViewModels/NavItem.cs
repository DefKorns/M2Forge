using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;

namespace M2Forge.App.ViewModels;

public sealed record NavItem(string Title, string Subtitle, MaterialIconKind Icon, ViewModelBase Page);
