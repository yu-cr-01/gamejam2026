# 卡表口径自检（v3.0）：素材 H 三档 / 稀有度派生 / V 档位派生 / 法术不得有 h·d·v
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$p = Join-Path $root 'UnityProject\Assets\Resources\Config\cards_v21.json'
$text = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
$j = $text | ConvertFrom-Json

$bad = 0
"素材 " + $j.materials.Count + " 张 · 法术 " + $j.spells.Count + " 张"

# ① 素材 H 只能 5/10/20，且 rarity 必须和 H 一致
$rar = @{ 5 = '普通'; 10 = '稀有'; 20 = '传说' }
foreach ($m in $j.materials) {
    if ($m.h -ne 5 -and $m.h -ne 10 -and $m.h -ne 20) {
        "✗ H 违规：" + $m.name + " h=" + $m.h; $bad++
    } elseif ($m.rarity -ne $rar[$m.h]) {
        "✗ 稀有度不符：" + $m.name + " h=" + $m.h + " 期望 " + $rar[$m.h] + " 实际 " + $m.rarity; $bad++
    }
    if ($m.type -ne '素材') { "✗ type 不够：$($m.name) type=$($m.type)"; $bad++ }
}

# ② V 档位派生：极低1/低2/中3/高5/极高8
$vg = @{ 1 = '极低'; 2 = '低'; 3 = '中'; 5 = '高'; 8 = '极高' }
foreach ($m in $j.materials) {
    if ($vg.ContainsKey([int]$m.v)) {
        if ($m.vGrade -ne $vg[[int]$m.v]) { "✗ V 档位不符：$($m.name) v=$($m.v) 期望 $($vg[[int]$m.v]) 实际 $($m.vGrade)"; $bad++ }
    } else {
        "✗ V 不在档位表里：$($m.name) v=$($m.v)"; $bad++
    }
}

# ③ 法术：只标稀有度，不得有 h/d/v 键；type 必须是「法术」
foreach ($s in $j.spells) {
    $names = ($s | Get-Member -MemberType NoteProperty).Name
    foreach ($k in @('h', 'd', 'v', 'vGrade')) {
        if ($names -contains $k) { "✗ 法术不该有 $k 字段：$($s.name)"; $bad++ }
    }
    if ($s.type -ne '法术') { "✗ 法术 type 不对：$($s.name) type=$($s.type)"; $bad++ }
    if ([string]::IsNullOrEmpty($s.rarity)) { "✗ 法术缺稀有度：$($s.name)"; $bad++ }
    if ($names -notcontains 'rarity') { "✗ 法术缺 rarity 键：$($s.name)"; $bad++ }
}

# ④ 素材/法术重名、id 重复
$all = @()
foreach ($m in $j.materials) { $all += ('素材|' + $m.id + '|' + $m.name) }
foreach ($s in $j.spells) { $all += ('法术|' + $s.id + '|' + $s.name) }
$dupName = $all | ForEach-Object { ($_ -split '\|')[2] } | Group-Object | Where-Object { $_.Count -gt 1 }
$dupId = $all | ForEach-Object { ($_ -split '\|')[1] } | Group-Object | Where-Object { $_.Count -gt 1 }
foreach ($d in $dupName) { "✗ 重名：$($d.Name) × $($d.Count)"; $bad++ }
foreach ($d in $dupId) { "✗ id 重复：$($d.Name) × $($d.Count)"; $bad++ }

# ⑤ 形态转换 / D耗尽 产出名必须在卡表里（灰烬这类要单独报）
$names = @{}
foreach ($m in $j.materials) { $names[$m.name] = '素材' }
foreach ($s in $j.spells) { $names[$s.name] = '法术' }
foreach ($m in $j.materials) {
    foreach ($t in $m.transitions) {
        if ([string]::IsNullOrEmpty($t.result)) { continue }
        foreach ($r in ($t.result -split '[、,，+（(]')) {
            $n = $r.Trim().TrimEnd('）', ')', '。')
            if ($n.Length -eq 0 -or $n.StartsWith('无') -or $n.Contains('归零') -or $n.Contains('空白卡')) { continue }
            if (-not $names.ContainsKey($n)) { "⚠ 形态转换产出不在卡表里：$($m.name) → 「$n」" }
        }
    }
    foreach ($e in $m.exhaust) {
        $n = ($e -replace '变为一张', '' -replace '随机法术卡', '').Trim()
        if ($n.Length -eq 0 -or $n.Contains('随机法术')) { continue }
        foreach ($r in ($n -split '[、,，+]')) {
            $x = $r.Trim()
            if ($x.Length -eq 0 -or $x.Contains('空白卡')) { continue }
            if (-not $names.ContainsKey($x)) { "⚠ D耗尽产出不在卡表里：$($m.name) → 「$x」" }
        }
    }
}

"违规/异常合计：$bad"
