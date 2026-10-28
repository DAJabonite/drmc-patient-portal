#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$SiteName,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$PoolName,
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$StorageRoot,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9.-]+$')][string]$HostName,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$CertificateThumbprint,
    [Parameter(Mandatory)][string]$SettingsFile
)
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

function FullPath([string]$Path) { [IO.Path]::GetFullPath($Path).TrimEnd('\') }
function IsWithin([string]$Child, [string]$Parent) {
    $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or $Child.StartsWith($Parent + '\', [StringComparison]::OrdinalIgnoreCase)
}
$publish = FullPath $PublishPath
$privateRoot = FullPath $StorageRoot
$settingsPath = FullPath $SettingsFile
if (-not (Test-Path -LiteralPath (Join-Path $publish 'DrmcPatientPortal.dll')) -or -not (Test-Path -LiteralPath (Join-Path $publish 'web.config'))) {
    throw 'PublishPath must contain a framework-dependent published portal and web.config.'
}
if ($privateRoot -eq [IO.Path]::GetPathRoot($privateRoot).TrimEnd('\') -or (IsWithin $privateRoot $publish) -or (IsWithin $publish $privateRoot)) {
    throw 'StorageRoot must be a dedicated directory separate from the published site and drive root.'
}
if (IsWithin $settingsPath $publish) { throw 'Keep the deployment settings file outside the published site.' }
foreach ($path in @($publish, $privateRoot, $settingsPath)) {
    $probePath = $path
    while ($probePath) {
        if ((Test-Path -LiteralPath $probePath) -and ((Get-Item -LiteralPath $probePath).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Deployment paths must not traverse junctions or symbolic links.'
        }
        $probePath = Split-Path -Path $probePath -Parent
    }
}
if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2')) { throw 'Install/repair the .NET 10 Hosting Bundle after IIS.' }
if (-not (Get-WebGlobalModule -Name 'ApplicationInitializationModule')) { throw 'Install the IIS Application Initialization feature.' }
$certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint"
if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) {
    throw 'The TLS certificate must have a private key and be within its validity period.'
}
$settings = @{}
foreach ($property in (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).PSObject.Properties) {
    if ($property.Name -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $property.Value -isnot [string]) { throw 'Settings must be a flat JSON object of environment-variable names and string values.' }
    $settings[$property.Name] = $property.Value
}
foreach ($required in @('ConnectionStrings__DefaultConnection', 'AllowedHosts', 'Portal__PublicOrigin', 'Notifications__Email__Host', 'Notifications__Email__FromAddress')) {
    if ([string]::IsNullOrWhiteSpace($settings[$required])) { throw "Missing required deployment setting: $required" }
}
if ($settings['Portal__PublicOrigin'].TrimEnd('/') -ne "https://$HostName") { throw 'PublicOrigin must match this HTTPS site hostname.' }
if ($settings['AllowedHosts'].Split(';') -notcontains $HostName -or $settings['AllowedHosts'].Contains('*')) { throw 'AllowedHosts must explicitly contain HostName without wildcards.' }
if ($settings['DataProtection__ProtectKeysWithDpapi'] -ne 'true' -and [string]::IsNullOrWhiteSpace($settings['DataProtection__CertificatePath'])) {
    throw 'Configure current-user DPAPI or an RSA key-protection certificate.'
}
$settings['ASPNETCORE_ENVIRONMENT'] = 'Production'
$settings['DOTNET_ENVIRONMENT'] = 'Production'
$settings['DevelopmentFixtures__Enabled'] = 'false'
$settings['AdminBootstrap__Enabled'] = 'false'
$settings['Identity__RequireConfirmedAccount'] = 'true'
$settings['Notifications__Email__UseSsl'] = 'true'
$settings['PortalOperations__MonitorWorkers'] = 'true'
$directories = [ordered]@{
    DataProtection__KeyRingPath = 'keys'
    PatientDocuments__RootPath = 'patient-documents'
    PatientDocuments__TemporaryPath = 'temporary-uploads'
    LabReports__RootPath = 'lab-reports'
    RadiologyReports__RootPath = 'radiology-reports'
    AdvisoryImages__RootPath = 'advisory-images'
    Imports__StagingPath = 'imports'
}
foreach ($key in $directories.Keys) { $settings[$key] = Join-Path $privateRoot $directories[$key] }
if (Test-Path -LiteralPath "IIS:\Sites\$SiteName") {
    if ((FullPath (Get-Item "IIS:\Sites\$SiteName").physicalPath) -ne $publish) { throw 'An existing site uses a different publish directory.' }
    if (Get-WebBinding -Name $SiteName | Where-Object { $_.protocol -ne 'https' }) {
        throw 'Review and remove non-HTTPS bindings from this dedicated portal site before applying configuration.'
    }
}
if ((Get-Website | Where-Object { $_.Name -ne $SiteName -and $_.applicationPool -eq $PoolName })) { throw 'Use a dedicated application pool.' }
if (-not $PSCmdlet.ShouldProcess("IIS site $SiteName, pool $PoolName, TLS host $HostName, publish and private-directory ACLs", 'Apply portal hosting configuration')) { return }

if (-not (Test-Path -LiteralPath "IIS:\AppPools\$PoolName")) { New-WebAppPool -Name $PoolName | Out-Null }
if ((Get-WebAppPoolState -Name $PoolName).Value -eq 'Started') { Stop-WebAppPool -Name $PoolName }
$poolPath = "IIS:\AppPools\$PoolName"
Set-ItemProperty $poolPath managedRuntimeVersion ''
Set-ItemProperty $poolPath enable32BitAppOnWin64 $false
Set-ItemProperty $poolPath startMode 'AlwaysRunning'
Set-ItemProperty $poolPath autoStart $true
Set-ItemProperty $poolPath processModel.identityType 'ApplicationPoolIdentity'
Set-ItemProperty $poolPath processModel.loadUserProfile $true
Set-ItemProperty $poolPath processModel.maxProcesses 1
Set-ItemProperty $poolPath processModel.idleTimeout ([TimeSpan]::Zero)
# Explicit maintenance/patch windows replace the default elapsed-time recycle.
Set-ItemProperty $poolPath recycling.periodicRestart.time ([TimeSpan]::Zero)

foreach ($path in @($privateRoot) + @($directories.Values | ForEach-Object { Join-Path $privateRoot $_ })) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @(@('S-1-5-18', 'FullControl'), @('S-1-5-32-544', 'FullControl'))) {
        $identity = New-Object Security.Principal.SecurityIdentifier($rule[0])
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity, $rule[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("IIS AppPool\$PoolName", 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $path -AclObject $acl
}
$publishAcl = Get-Acl -LiteralPath $publish
$publishAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("IIS AppPool\$PoolName", 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath $publish -AclObject $publishAcl

$filter = "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables"
foreach ($key in $settings.Keys) {
    $itemFilter = "$filter/add[@name='$key']"
    if (Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $itemFilter) {
        Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $itemFilter -Name value -Value $settings[$key]
    }
    else { Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -Value @{ name=$key; value=$settings[$key] } }
}
if (-not (Test-Path -LiteralPath "IIS:\Sites\$SiteName")) {
    New-Website -Name $SiteName -PhysicalPath $publish -ApplicationPool $PoolName -Port 443 -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null
}
Set-ItemProperty "IIS:\Sites\$SiteName" applicationPool $PoolName
Set-ItemProperty "IIS:\Sites\$SiteName" serverAutoStart $true
Set-ItemProperty "IIS:\Sites\$SiteName" applicationDefaults.preloadEnabled $true
if (-not (Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName)) {
    New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1
}
(Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName).AddSslCertificate($CertificateThumbprint, 'My')
Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/applicationInitialization' -Name doAppInitAfterRestart -Value $true
Write-Output 'IIS configuration applied. Pool remains stopped: complete offline protection maintenance under the deployment identity, then start and verify readiness.'
