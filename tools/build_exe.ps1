# Builds MountSheetSync.exe on Linux with the Roslyn compiler that ships inside PowerShell 7,
# against the .NET Framework 4.8 reference assemblies from mono (/usr/lib/mono/4.8-api).
# Same switches as build.cmd: C# 5, winexe, app.ico + app.manifest, embedded icons and catalog.
param(
    [Parameter(Mandatory = $true)][string]$Src,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$RefDir = '/usr/lib/mono/4.8-api'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll')
Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll')
if (-not ('MssStreamFactory' -as [type])) {
    Add-Type -TypeDefinition @'
public static class MssStreamFactory {
    public static System.Func<System.IO.Stream> From(byte[] bytes) {
        return () => new System.IO.MemoryStream(bytes, false);
    }
}
'@
}

$utf8 = [System.Text.UTF8Encoding]::new($false)
$parse = [Microsoft.CodeAnalysis.CSharp.CSharpParseOptions]::new([Microsoft.CodeAnalysis.CSharp.LanguageVersion]::CSharp5)
$trees = [System.Collections.Generic.List[Microsoft.CodeAnalysis.SyntaxTree]]::new()
foreach ($f in (Get-ChildItem -Path $Src -Filter '*.cs' | Sort-Object Name)) {
    $text = [Microsoft.CodeAnalysis.Text.SourceText]::From([System.IO.File]::ReadAllText($f.FullName, $utf8), $utf8)
    $trees.Add([Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($text, $parse, $f.Name))
}

$refs = [System.Collections.Generic.List[Microsoft.CodeAnalysis.MetadataReference]]::new()
foreach ($n in 'mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Security', 'System.IO.Compression') {
    $refs.Add([Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile((Join-Path $RefDir "$n.dll")))
}

$opts = [Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions]::new([Microsoft.CodeAnalysis.OutputKind]::WindowsApplication)
$opts = $opts.WithOptimizationLevel([Microsoft.CodeAnalysis.OptimizationLevel]::Release).WithPlatform([Microsoft.CodeAnalysis.Platform]::AnyCpu)
$comp = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('MountSheetSync', $trees, $refs, $opts)

$manifest = [System.IO.MemoryStream]::new([System.IO.File]::ReadAllBytes((Join-Path $Src 'app.manifest')))
$icon = [System.IO.MemoryStream]::new([System.IO.File]::ReadAllBytes((Join-Path $Src 'app.ico')))
$win32 = $comp.CreateDefaultWin32Resources($true, $false, $manifest, $icon)

$res = [System.Collections.Generic.List[Microsoft.CodeAnalysis.ResourceDescription]]::new()
foreach ($pair in @(@('app-small.ico', 'MountSheetSync.app.ico'), @('warn-small.ico', 'MountSheetSync.warn.ico'), @('catalog.json', 'MountSheetSync.catalog.json'), @('i18n.json', 'MountSheetSync.i18n.json'), @('sheet-i18n.json', 'MountSheetSync.sheet-i18n.json'))) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $Src $pair[0]))
    $res.Add([Microsoft.CodeAnalysis.ResourceDescription]::new($pair[1], [MssStreamFactory]::From($bytes), $true))
}

$pe = [System.IO.MemoryStream]::new()
$result = $comp.Emit($pe, $null, $null, $win32, $res)
$shown = 0
foreach ($d in $result.Diagnostics) {
    if ($d.Severity -ge [Microsoft.CodeAnalysis.DiagnosticSeverity]::Warning -and $shown -lt 60) {
        Write-Output $d.ToString()
        $shown++
    }
}
if (-not $result.Success) {
    Write-Output 'BUILD FAILED'
    exit 1
}
[System.IO.File]::WriteAllBytes($Out, $pe.ToArray())
Write-Output ("OK " + $Out + " " + $pe.Length + " bytes")
