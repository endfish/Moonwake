[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceRoot, [Parameter(Mandatory)][string]$AssetDirectory)
$ErrorActionPreference = 'Stop'
$language = Get-Content -LiteralPath (Join-Path $AssetDirectory 'UIRes/loc/dalamud/dalamud_zh.json') -Raw | ConvertFrom-Json -AsHashtable
$allowed = @('NotificationConstants.DefaultInitiator', 'ContactInformationDiscordUrl')
$missing = [Collections.Generic.HashSet[string]]::new()
$placeholderErrors = [Collections.Generic.HashSet[string]]::new()
$pattern = '(?:LazyLoc|Loc)\.Localize\(\s*"(?<key>[^"\\]+)"\s*,\s*"(?<fallback>(?:\\.|[^"\\])*)"'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'Dalamud') -Recurse -Filter '*.cs') {
    foreach ($match in [regex]::Matches([IO.File]::ReadAllText($file.FullName), $pattern)) {
        $key = $match.Groups['key'].Value
        if ($key -in $allowed -or $match.Groups['fallback'].Value -notmatch '[A-Za-z]{3}') { continue }
        if ($language[$key].message -notmatch '[\u4e00-\u9fff]') { [void]$missing.Add($key) }
        $placeholderPattern = '(?<!\{)\{(\d+)(?:[,:][^}]*)?\}(?!\})'
        $expected = @([regex]::Matches($match.Groups['fallback'].Value, $placeholderPattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ','
        $actual = @([regex]::Matches([string]$language[$key].message, $placeholderPattern) | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) -join ','
        if ($expected -ne $actual) { [void]$placeholderErrors.Add($key) }
    }
}
$chatEnum = Get-Content -LiteralPath (Join-Path $SourceRoot 'Dalamud/Game/Text/XivChatType.cs') -Raw
foreach ($match in [regex]::Matches($chatEnum, '(?m)^\s+(\w+) = \d+,')) {
    $name = $match.Groups[1].Value
    if ($name -eq 'Debug') { continue }
    if ($language["MoonwakeChat$name"].message -notmatch '[\u4e00-\u9fff]') { [void]$missing.Add("MoonwakeChat$name") }
}
$colorEnum = Get-Content -LiteralPath (Join-Path $SourceRoot 'imgui/Dalamud.Bindings.ImGui/Generated/Enums/ImGuiCol.cs') -Raw
foreach ($match in [regex]::Matches($colorEnum, '(?m)^\s+(\w+) = unchecked')) {
    $name = $match.Groups[1].Value
    if ($name -eq 'Count') { continue }
    if ($language["MoonwakeColor$name"].message -notmatch '[\u4e00-\u9fff]') { [void]$missing.Add("MoonwakeColor$name") }
}
if ($missing.Count -gt 0) { throw "Untranslated UI keys: $(@($missing | Sort-Object) -join ', ')" }
if ($placeholderErrors.Count -gt 0) { throw "Translation placeholder mismatch: $(@($placeholderErrors | Sort-Object) -join ', ')" }
Write-Output 'PASS: localized string keys, chat channel labels and style color names have Chinese resources.'
