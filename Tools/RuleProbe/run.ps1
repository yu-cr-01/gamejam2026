# 规则内核离线校验 —— 不用开 Unity，直接编译运行
#
# 【为什么要有这个】
#   层数/形态转换/启动/献祭这些规则是**纯逻辑**（不碰渲染、不碰 GameObject），
#   那就没有理由为了验证一条规则去开编辑器、等它加载、点 Play。
#   这里把 Rules 内核 + 卡表数据编成控制台程序直接跑，
#   每条规则一个断言 —— 改规则后跑一下就知道有没有踩坏别的东西。
#
# 用法：pwsh -File Tools/RuleProbe/run.ps1
$ErrorActionPreference = 'Stop'

$unity  = 'D:\unity\2022.3.62f3c1\Editor\Data'
$csc    = "$unity\DotNetSdkRoslyn\csc.dll"
$dotnet = "$unity\NetCoreRuntime\dotnet.exe"
$proj   = Join-Path $PSScriptRoot '..\..\UnityProject'
$fx     = (Get-ChildItem "$unity\NetCoreRuntime\shared\Microsoft.NETCore.App" -Directory |
           Sort-Object Name -Descending | Select-Object -First 1).FullName
$work   = Join-Path $env:TEMP 'ruleprobe'
New-Item -ItemType Directory -Force -Path $work | Out-Null

$src = @()
# 规则内核（纯逻辑，不依赖 UnityEngine）
# ★ 不要写成 `$src += Get-ChildItem ... | ForEach-Object {...}`：
#   PowerShell 会把管道接到赋值表达式上（$src 收进去的是 FileInfo，不是路径），
#   表现是"明明有文件，csc 却说命名空间不存在"。这里显式逐条加。
foreach ($f in (Get-ChildItem "$proj\Assets\Scripts\Rules" -Filter *.cs -ErrorAction SilentlyContinue)) {
    $src += $f.FullName
}
# 卡表数据（形态/标签/转换/献祭的描述）
$src += "$proj\Assets\Scripts\Data\Cards\FormChange.cs"
$src += "$proj\Assets\Scripts\Data\Ingredients\Ingredient.cs"
$src += "$proj\Assets\Scripts\Data\Attributes\AttrId.cs"
$src += "$proj\Assets\Scripts\Data\Attributes\AttrSet.cs"
$src += "$proj\Assets\Scripts\Data\Effects\Effect.cs"
$src += "$proj\Assets\Scripts\Data\Effects\EffectOp.cs"
$src += "$proj\Assets\Scripts\Data\Effects\EffectGroup.cs"
$src += "$proj\Assets\Scripts\Data\Attributes\AttrCatalog.cs"
$src += "$proj\Assets\Scripts\Data\Attributes\AttrDef.cs"
$src += "$PSScriptRoot\RuleProbe.cs"

$refs = @("/r:$unity\Managed\UnityEngine\UnityEngine.CoreModule.dll",
          "/r:$unity\Managed\UnityEngine\UnityEngine.dll")
$refs += Get-ChildItem $fx -Filter 'System*.dll' |
         Where-Object { $_.Name -notlike '*Native*' } | ForEach-Object { "/r:$($_.FullName)" }
$refs += "/r:$fx\netstandard.dll"

$outDll = "$work\ruleprobe.dll"
Write-Host ("源文件 " + $src.Count + " 个：")
foreach ($s in $src) { Write-Host ("  " + (Split-Path $s -Leaf)) }
& $dotnet exec $csc /nostdlib /noconfig /target:exe /langversion:9.0 @refs "/out:$outDll" @src
if ($LASTEXITCODE -ne 0) { throw "编译失败" }

$ver = Split-Path $fx -Leaf
[System.IO.File]::WriteAllText("$work\ruleprobe.runtimeconfig.json",
  '{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "' + $ver + '" } } }')
Copy-Item "$unity\Managed\UnityEngine\UnityEngine.CoreModule.dll" $work -Force
Copy-Item "$unity\Managed\UnityEngine\UnityEngine.dll" $work -Force -ErrorAction SilentlyContinue

& $dotnet $outDll $proj
exit $LASTEXITCODE
