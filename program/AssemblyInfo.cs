using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: AssemblyTitle("Mount Sheet Sync")]
[assembly: AssemblyDescription("Keeps a Google sheet of World of Warcraft mounts from the addon data: boss kills, new mounts, characters")]
[assembly: AssemblyProduct("Mount Sheet Sync")]
[assembly: AssemblyVersion("2.3.0.0")]
[assembly: AssemblyFileVersion("2.3.0.0")]
[assembly: AssemblyInformationalVersion("2.3.0")]
[assembly: NeutralResourcesLanguage("ru-RU")]
[assembly: ComVisible(false)]

// Без этого атрибута .NET Framework включает режим совместимости со старыми версиями
// (в том числе устаревший TLS), и Google не примет соединение.
[assembly: TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]
