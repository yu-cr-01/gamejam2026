# CupSim 规格校验 —— 不用开 Unity，直接编译运行
#
# 【为什么能脱离 Unity】
# CupSim 是纯逻辑，只用 Vector2 / Mathf 这些 UnityEngine 的数学类型
# （在 CoreModule 里，不需要编辑器）。所以可以把它连同数据层一起编成
# 控制台程序直接跑，不占工程锁、不用等编辑器启动。
#
# 用法：pwsh -File Tools/SimProbe/run.ps1
$ErrorActionPreference = 'Stop'

$unity = 'D:\unity\2022.3.62f3c1\Editor\Data'
$csc    = "$unity\DotNetSdkRoslyn\csc.dll"
$dotnet = "$unity\NetCoreRuntime\dotnet.exe"
$proj   = Join-Path $PSScriptRoot '..\..\UnityProject'
$fx     = (Get-ChildItem "$unity\NetCoreRuntime\shared\Microsoft.NETCore.App" -Directory |
           Sort-Object Name -Descending | Select-Object -First 1).FullName
$work   = Join-Path $env:TEMP 'simprobe'
New-Item -ItemType Directory -Force -Path $work | Out-Null

$src = @()
foreach ($d in 'Attributes','Effects','Choices','Levels') {
  $src += Get-ChildItem "$proj\Assets\Scripts\Data\$d" -Filter *.cs | ForEach-Object { $_.FullName }
}
$src += "$proj\Assets\Scripts\Data\Ingredients\Ingredient.cs"
$src += "$proj\Assets\Scripts\Data\Modules\SpeedModule.cs"
$src += "$proj\Assets\Scripts\Data\Decks\Deck.cs"
$src += "$proj\Assets\Scripts\Data\Session\TurnState.cs"
$src += "$proj\Assets\Scripts\Sim\CupSim.cs"
$src += "$PSScriptRoot\SimProbe.cs"

$refs = @("/r:$unity\Managed\UnityEngine\UnityEngine.CoreModule.dll",
          "/r:$unity\Managed\UnityEngine\UnityEngine.dll")
# 只要托管程序集 —— *.Native.dll 里没有托管元数据，喂给 csc 会直接报错
$refs += Get-ChildItem $fx -Filter 'System*.dll' |
         Where-Object { $_.Name -notlike '*Native*' } | ForEach-Object { "/r:$($_.FullName)" }
$refs += "/r:$fx\netstandard.dll"

$outDll = "$work\simprobe.dll"
& $dotnet exec $csc /nostdlib /noconfig /target:exe /langversion:9.0 @refs "/out:$outDll" @src
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

$ver = Split-Path $fx -Leaf
[System.IO.File]::WriteAllText("$work\simprobe.runtimeconfig.json",
  '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "' + $ver + '" } } }')
Copy-Item "$unity\Managed\UnityEngine\UnityEngine.CoreModule.dll" $work -Force
Copy-Item "$unity\Managed\UnityEngine\UnityEngine.dll" $work -Force -ErrorAction SilentlyContinue

& $dotnet $outDll
exit $LASTEXITCODE