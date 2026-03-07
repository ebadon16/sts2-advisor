$gameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64'

# Load all DLLs from game dir to resolve dependencies
$dlls = Get-ChildItem "$gameDir\*.dll" | Where-Object { $_.Name -ne 'sts2.dll' }
foreach ($dll in $dlls) {
    try { [System.Reflection.Assembly]::LoadFrom($dll.FullName) | Out-Null } catch {}
}

$asm = [System.Reflection.Assembly]::LoadFrom("$gameDir\sts2.dll")

try {
    $types = $asm.GetTypes()
    Write-Host "Loaded all $($types.Length) types"
} catch [System.Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
    Write-Host "Loaded $($types.Count) types (partial)"
}

Write-Host ""

# Key classes to inspect
$targetNames = @(
    'NCardRewardSelectionScreen',
    'NChooseARelicSelection',
    'NMerchantInventory',
    'NRewardsScreen',
    'RunState',
    'RunManager',
    'CardModel',
    'Player',
    'RelicModel'
)

foreach ($name in $targetNames) {
    $matches = @($types | Where-Object { $_.Name -eq $name })
    if ($matches.Count -eq 0) {
        $matches = @($types | Where-Object { $_.Name -like "*$name*" -and $_.Name -notlike '*<*' })
    }

    foreach ($t in $matches) {
        Write-Host "============================================"
        Write-Host "CLASS: $($t.FullName)"
        Write-Host "BASE: $($t.BaseType)"
        Write-Host "--------------------------------------------"

        # Properties
        try {
            $props = $t.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static)
            if ($props.Count -gt 0) {
                Write-Host "PROPERTIES:"
                foreach ($p in $props | Select-Object -First 30) {
                    Write-Host "  $($p.PropertyType.Name) $($p.Name) { $(if($p.CanRead){'get'}) $(if($p.CanWrite){'set'}) }"
                }
            }
        } catch {}

        # Fields
        try {
            $fields = $t.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static)
            if ($fields.Count -gt 0) {
                Write-Host "FIELDS:"
                foreach ($f in $fields | Select-Object -First 30) {
                    $static = if ($f.IsStatic) { 'static ' } else { '' }
                    Write-Host "  ${static}$($f.FieldType.Name) $($f.Name)"
                }
            }
        } catch {}

        # Methods (non-inherited, exclude getters/setters)
        try {
            $methods = $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly) | Where-Object { -not $_.IsSpecialName }
            if ($methods.Count -gt 0) {
                Write-Host "METHODS:"
                foreach ($m in $methods | Select-Object -First 30) {
                    $static = if ($m.IsStatic) { 'static ' } else { '' }
                    $params = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
                    Write-Host "  ${static}$($m.ReturnType.Name) $($m.Name)($params)"
                }
            }
        } catch {}

        Write-Host ""
    }
}
