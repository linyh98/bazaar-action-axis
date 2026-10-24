param(
    [string]$GameRoot = 'D:/Program Files (x86)/Steam/steamapps/common/The Bazaar',
    [string]$Fixture = 'tools/actionaxis_plugin/tests/fixtures/native_kyver_monster.json'
)

# Read-only evidence: deserialize the real fixture with the installed game's
# own Newtonsoft settings and call its native tier accessors. No game process,
# game state, engine source, or template data is modified.
$ErrorActionPreference = 'Stop'
$managed = Join-Path $GameRoot 'TheBazaar_Data/Managed'
[void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'Newtonsoft.Json.dll'))
$shared = [Reflection.Assembly]::LoadFrom((Join-Path $managed 'BazaarGameShared.dll'))
$fixtureJson = Get-Content -LiteralPath $Fixture -Raw | ConvertFrom-Json
$skillId = 'c5bff6ad-5d13-4cf3-9941-98f3cf913949'
$templateJson = $fixtureJson.cards.$skillId | ConvertTo-Json -Depth 100
$nativeType = $shared.GetType('BazaarGameShared.Domain.Cards.Skill.TCardSkill')
$settingsType = $shared.GetType('BazaarGameShared.Infra.Serialization.BazaarJsonSerializerSettingsClient')
$settings = [Activator]::CreateInstance($settingsType)
$native = [Newtonsoft.Json.JsonConvert]::DeserializeObject($templateJson, $nativeType, $settings)
$tierType = $shared.GetType('BazaarGameShared.Domain.Core.Types.ETier')
$attributeType = $shared.GetType('BazaarGameShared.Domain.Core.Types.ECardAttributeType')
$chargeAttribute = [Enum]::Parse($attributeType, 'ChargeAmount')

'## Native Counterstrike tier probe'
"Monster skill tier: $($fixtureJson.monster.Player.Skills[0].Tier)"
"Monster skill TemplateVersion: $($fixtureJson.monster.Player.Skills[0].TemplateVersion)"
"Template Version: $($native.Version)"
"StartingTier: $($native.StartingTier); actual Tiers keys: $($native.Tiers.Keys -join ', ')"
foreach ($label in @('Bronze', 'Silver', 'Gold', 'Diamond', 'Legendary')) {
    $tier = [Enum]::Parse($tierType, $label)
    $tierArguments = [object[]]@($tier, $null)
    $exists = $nativeType.GetMethod('TryGetTierTemplate').Invoke($native, $tierArguments)
    $charge = $nativeType.GetMethod('GetAttributeBaseValueAtTier').Invoke($native, [object[]]@($chargeAttribute, $tier))
    if ($null -eq $charge) { $charge = '<null>' }
    "${label}: exists=$exists; abilities=$($native.GetAbilityTemplatesByTier($tier).Count); auras=$($native.GetAuraTemplatesByTier($tier).Count); ChargeAmount=$charge"
}

[void][Reflection.Assembly]::LoadFrom((Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll'))
function Get-AllMetadataTypes($types) {
    foreach ($type in $types) {
        $type
        Get-AllMetadataTypes $type.NestedTypes
    }
}

$targets = @{
    'BazaarGameShared.dll' = @{
        'BazaarGameShared.Domain.Cards.Skill.TCardSkill' = @('TryGetTierTemplate', 'GetAttributeBaseValueAtTier', 'GetAbilityTemplatesByTier', 'GetAuraTemplatesByTier')
        'BazaarGameShared.Infra.Serialization.BazaarJsonSerializerSettingsClient' = @('.ctor')
    }
    'BazaarGameClient.dll' = @{
        'BazaarGameClient.Domain.Models.Cards.Card' = @('set_Tier', '<GetAbilitiesByTier>g__TryGetTierTemplate|67_0')
        'BazaarGameClient.Domain.Models.Cards.Card/<GetAbilitiesByTier>d__67' = @('MoveNext')
    }
    'TheBazaarRuntime.dll' = @{
        'TheBazaar.DTOUtils' = @('PopulateTemplateAsync')
        'TheBazaar.DataManagement.Json.JsonGameDataManager' = @('GetCardById')
    }
}
foreach ($assemblyName in $targets.Keys | Sort-Object) {
    $path = Join-Path $managed $assemblyName
    $metadata = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($path)
    "## $assemblyName SHA256=$((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)"
    foreach ($type in Get-AllMetadataTypes $metadata.MainModule.Types) {
        if (!$targets[$assemblyName].ContainsKey($type.FullName)) { continue }
        foreach ($method in $type.Methods) {
            if ($method.HasBody -and $targets[$assemblyName][$type.FullName].Contains($method.Name)) {
                "### $($method.FullName)"
                $method.Body.Instructions | ForEach-Object { $_.ToString() }
            }
        }
    }
    $metadata.Dispose()
}
