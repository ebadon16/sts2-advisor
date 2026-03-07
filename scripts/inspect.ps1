$dllDir = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'

# Pre-load dependencies
try { [System.Reflection.Assembly]::LoadFrom("$dllDir\GodotSharp.dll") | Out-Null } catch {}
try { [System.Reflection.Assembly]::LoadFrom("$dllDir\0Harmony.dll") | Out-Null } catch {}

$asm = [System.Reflection.Assembly]::LoadFrom("$dllDir\sts2.dll")

try {
    $types = $asm.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
    Write-Host "Loaded $($types.Count) types (some failed to load)"
    Write-Host ''
}

$searchTerms = @('CardReward', 'RewardScreen', 'CardChoice', 'CombatReward', 'PostCombat', 'RelicReward', 'BossRelic', 'RelicChoice', 'RelicSelect', 'ShopScreen', 'Merchant', 'Store', 'ShopItem', 'GameManager', 'AppManager', 'RunState', 'RunData', 'MasterDeck', 'Player', 'CharacterData', 'Card', 'Relic', 'Deck')

foreach ($term in $searchTerms) {
    $matches = @($types | Where-Object { $_.Name -like "*$term*" -and $_.Name -notlike '*__*' -and $_.Name -notlike '*<*' })
    if ($matches.Count -gt 0) {
        Write-Host "=== '$term' ($($matches.Count)) ==="
        $matches | Select-Object -First 15 | ForEach-Object {
            $base = if ($_.BaseType) { $_.BaseType.Name } else { 'none' }
            Write-Host "  $($_.FullName) : $base"
        }
        Write-Host ''
    }
}
