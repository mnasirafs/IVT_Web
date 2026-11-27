# Builds the web project and runs Vera proposal and query verification against memory/recording fakes.
# No database, mail or Azure is touched. Transaction commit and notification delivery require integration testing.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here '..\..')
$web = Join-Path $repo 'AFSDataGateway'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
$csc = Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe'

if (-not $env:IVT_SKIP_BUILD) {
    & $msbuild (Join-Path $web 'IVT.csproj') /t:Build /p:Configuration=Debug /v:q /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Web project build failed.' }
}
$out = Join-Path $here 'bin'
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($dll in 'AFSDataGateway.dll', 'Newtonsoft.Json.dll', 'EntityFramework.dll') { Copy-Item (Join-Path $web "bin\$dll") $out -Force }
$fx = "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8"
if (-not (Test-Path $fx)) { $fx = "${env:WINDIR}\Microsoft.NET\Framework64\v4.0.30319" }
$refs = @('System.dll', 'System.Core.dll', 'System.Data.dll', 'System.Web.dll', 'System.Net.Http.dll', 'Microsoft.CSharp.dll', 'mscorlib.dll', 'System.Runtime.dll', 'netstandard.dll') |
    ForEach-Object { $p = Join-Path $fx $_; if (-not (Test-Path $p)) { $p = Join-Path "$fx\Facades" $_ }; if (Test-Path $p) { "/r:$p" } }
$exe = Join-Path $out 'Chat.Verification.exe'
& $csc /nologo /noconfig /nostdlib+ /target:exe "/out:$exe" @refs `
    "/r:$(Join-Path $out 'AFSDataGateway.dll')" "/r:$(Join-Path $out 'Newtonsoft.Json.dll')" "/r:$(Join-Path $out 'EntityFramework.dll')" (Join-Path $here 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Verification suite failed to compile.' }
& $exe
exit $LASTEXITCODE

