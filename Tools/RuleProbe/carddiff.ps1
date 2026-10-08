# 卡表新旧结构比对（一次性临时工具，产出 docs 报告用；用完可留档）
# 用法：powershell -NoProfile -File Tools\RuleProbe\carddiff.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Load-Json([string]$p) {
    return ([System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8) | ConvertFrom-Json)
}

$new = Load-Json (Join-Path $root 'UnityProject\Assets\Resources\Config\cards_v21.json')
$oldPath = Join-Path $env:TEMP 'v21check\cards_old.json'
$old = Load-Json $oldPath

function Tags($o) { if ($null -eq $o) { return '' } ; return ($o -join '·') }
function Trans($o) {
    if ($null -eq $o) { return '' }
    $parts = @()
    foreach ($t in $o) { $parts += ($t.trigger + '→' + $t.result) }
    return ($parts -join '；')
}
function Exh($o) { if ($null -eq $o) { return '' } ; return ($o -join '+') }

$out = New-Object System.Collections.ArrayList

[void]$out.Add('=== 素材：新增 / 删除 ===')
$oldNames = @{}; foreach ($m in $old.materials) { $oldNames[$m.name] = $m }
$newNames = @{}; foreach ($m in $new.materials) { $newNames[$m.name] = $m }
foreach ($m in $new.materials) { if (-not $oldNames.ContainsKey($m.name)) { [void]$out.Add('新增素材：' + $m.name + '（' + $m.id + '）') } }
foreach ($m in $old.materials) { if (-not $newNames.ContainsKey($m.name)) { [void]$out.Add('删除素材：' + $m.name + '（' + $m.id + '）') } }

[void]$out.Add('')
[void]$out.Add('=== 素材：逐字段差异 ===')
foreach ($m in $new.materials) {
    $o = $oldNames[$m.name]
    if ($null -eq $o) { continue }
    $lines = @()
    if ($o.id -ne $m.id) { $lines += "id: $($o.id) → $($m.id)" }
    if ($o.series -ne $m.series) { $lines += "系列: $($o.series) → $($m.series)" }
    if ($o.form -ne $m.form) { $lines += "形态: $($o.form) → $($m.form)" }
    if ((Tags $o.tags) -ne (Tags $m.tags)) { $lines += "标签: [$((Tags $o.tags))] → [$((Tags $m.tags))]" }
    if ((Trans $o.transitions) -ne (Trans $m.transitions)) { $lines += "形态转换: [$((Trans $o.transitions))] → [$((Trans $m.transitions))]" }
    if ((Exh $o.exhaust) -ne (Exh $m.exhaust)) { $lines += "D耗尽: [$((Exh $o.exhaust))] → [$((Exh $m.exhaust))]" }
    if ($o.startup -ne $m.startup) { $lines += "启动: 「$($o.startup)」 → 「$($m.startup)」" }
    if ($o.sacrifice -ne $m.sacrifice) { $lines += "献祭: 「$($o.sacrifice)」 → 「$($m.sacrifice)」" }
    if (($o.h -ne $m.h) -or ($o.d -ne $m.d) -or ($o.v -ne $m.v)) { $lines += "H/D/V: $($o.h)/$($o.d)/$($o.v) → $($m.h)/$($m.d)/$($m.v)" }
    if ($o.vGrade -ne $m.vGrade) { $lines += "V档位: $($o.vGrade) → $($m.vGrade)" }
    if ($lines.Count -gt 0) {
        [void]$out.Add('· ' + $m.name)
        foreach ($l in $lines) { [void]$out.Add('    ' + $l) }
    }
}

[void]$out.Add('')
[void]$out.Add('=== 法术：新增 / 删除 ===')
$oldSp = @{}; foreach ($s in $old.spells) { $oldSp[$s.name] = $s }
$newSp = @{}; foreach ($s in $new.spells) { $newSp[$s.name] = $s }
foreach ($s in $new.spells) { if (-not $oldSp.ContainsKey($s.name)) { [void]$out.Add('新增法术：' + $s.name + '（' + $s.id + '）') } }
foreach ($s in $old.spells) { if (-not $newSp.ContainsKey($s.name)) { [void]$out.Add('删除法术：' + $s.name + '（' + $s.id + '）') } }

[void]$out.Add('')
[void]$out.Add('=== 法术：逐字段差异 ===')
foreach ($s in $new.spells) {
    $o = $oldSp[$s.name]
    $lines = @()
    if ($null -ne $o) {
        if ($o.id -ne $s.id) { $lines += "id: $($o.id) → $($s.id)" }
        if ($o.enchant -ne $s.enchant) { $lines += "附魔: [$($o.enchant)] → [$($s.enchant)]" }
        if ($o.category -ne $s.category) { $lines += "分类: [$($o.category)] → [$($s.category)]" }
        if ($o.requirement -ne $s.requirement) { $lines += "需求: 「$($o.requirement)」 → 「$($s.requirement)」" }
    } else {
        $lines += '（v2.1 没有这张法术）'
    }
    $lines += "稀有度: " + $s.rarity
    if ($lines.Count -gt 0) {
        [void]$out.Add('· ' + $s.name)
        foreach ($l in $lines) { [void]$out.Add('    ' + $l) }
    }
}

$text = ($out -join "`r`n")
[System.IO.File]::WriteAllText((Join-Path $env:TEMP 'v21check\carddiff.txt'), $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Host $text
