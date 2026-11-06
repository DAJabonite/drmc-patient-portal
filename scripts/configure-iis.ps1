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
    [Parameter(Mandatory)][string]$SettingsFile,
    [switch]$AllowStoragePathChange
)
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    $failure = [InvalidOperationException]::new($Message)
    $failure.Data['PortalDeploymentSafeMessage'] = $true
    throw $failure
}
function FullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\]+\\[^\\]+(?:\\|$))') { Fail 'Deployment paths must be absolute Windows paths.' }
    [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}
function IsWithin([string]$Child, [string]$Parent) {
    $Child.Equals($Parent, [StringComparison]::OrdinalIgnoreCase) -or $Child.StartsWith($Parent + '\', [StringComparison]::OrdinalIgnoreCase)
}
function AssertSeparate([string]$Path, [string]$Other) {
    if ((IsWithin $Path $Other) -or (IsWithin $Other $Path)) { Fail 'Deployment storage, secrets and published files must use separate directories.' }
}
function AssertNoLinks([string]$Path, [switch]$Descendants) {
    for ($current = $Path; $current; $current = Split-Path -Path $current -Parent) {
        if ((Test-Path -LiteralPath $current) -and ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { Fail 'Deployment paths must not traverse junctions or symbolic links.' }
    }
    if ($Descendants -and (Test-Path -LiteralPath $Path -PathType Container)) {
        $pending = [Collections.Generic.Stack[string]]::new()
        $pending.Push($Path)
        while ($pending.Count) {
            foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { Fail 'Deployment directories must not contain junctions or symbolic links.' }
                if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            }
        }
    }
}
function ReadSettings([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -gt 1MB) { Fail 'SettingsFile must be a flat JSON file of at most 1 MB.' }
    $raw = [IO.File]::ReadAllText($Path)
    $jsonString = '"(?:[^"\\\x00-\x1F]|\\(?:["\\/bfnrt]|u[0-9a-fA-F]{4}))*"'
    $pair = "$jsonString\s*:\s*$jsonString"
    if ($raw -notmatch "\A\s*\{\s*(?:$pair(?:\s*,\s*$pair)*)?\s*\}\s*\z") { Fail 'SettingsFile must be a flat JSON object of environment-variable names and string values.' }
    $result = @{}
    foreach ($match in [regex]::Matches($raw, "($jsonString)\s*:\s*($jsonString)")) {
        $key = ('{"v":' + $match.Groups[1].Value + '}') | ConvertFrom-Json
        $value = ('{"v":' + $match.Groups[2].Value + '}') | ConvertFrom-Json
        if ($key.v -notmatch '^[A-Za-z][A-Za-z0-9_]*$' -or $result.ContainsKey($key.v)) { Fail 'SettingsFile contains an invalid or duplicate environment-variable name.' }
        $result[$key.v] = $value.v
    }
    return $result
}
function BooleanSetting([hashtable]$Settings, [string]$Key, [bool]$Default) {
    if (-not $Settings.ContainsKey($Key)) { return $Default }
    $value = $false
    if (-not [bool]::TryParse($Settings[$Key], [ref]$value)) { Fail "$Key must be true or false." }
    return $value
}
function IntegerSetting([hashtable]$Settings, [string]$Key, [int]$Default, [int]$Minimum, [int]$Maximum) {
    $value = $Default
    if ($Settings.ContainsKey($Key) -and -not [int]::TryParse($Settings[$Key], [ref]$value)) { Fail "$Key must be an integer." }
    if ($value -lt $Minimum -or $value -gt $Maximum) { Fail "$Key is outside its supported range." }
    return $value
}
function AssertRestrictedAcl([string]$Path, [bool]$SecretFile, [bool]$RequirePoolAccess = $true) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $broad = @('S-1-1-0','S-1-2-0','S-1-2-1','S-1-5-4','S-1-5-7','S-1-5-11','S-1-5-32-545','S-1-5-32-546','S-1-5-32-568')
    foreach ($rule in (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        $access = [Security.AccessControl.FileSystemRights]::ReadData -bor [Security.AccessControl.FileSystemRights]::WriteData -bor
            [Security.AccessControl.FileSystemRights]::AppendData -bor [Security.AccessControl.FileSystemRights]::Delete -bor
            [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
            [Security.AccessControl.FileSystemRights]::TakeOwnership
        $requiredAccess = if ($SecretFile) { [Security.AccessControl.FileSystemRights]::ReadAndExecute } else { [Security.AccessControl.FileSystemRights]::Modify }
        if ($RequirePoolAccess -and $rule.AccessControlType -eq 'Deny' -and ($rule.FileSystemRights -band $requiredAccess) -and
            ($broad -contains $rule.IdentityReference.Value -or ($null -ne $poolSid -and $rule.IdentityReference -eq $poolSid))) {
            Fail 'A private storage or certificate ACL denies required pool access. Review that ACL before deployment.'
        }
        if ($rule.AccessControlType -eq 'Allow' -and $broad -contains $rule.IdentityReference.Value -and ($rule.FileSystemRights -band $access)) {
            if ($SecretFile) { Fail 'Restrict broad read access to deployment settings and certificate/password files before deployment.' }
            Fail 'Private storage has broad read access. Review its existing ACLs before deployment; this helper will not recursively reset them.'
        }
    }
}
function AssertPrivateTree([string]$Path, [string[]]$ManagedPaths = @()) {
    AssertNoLinks $Path -Descendants
    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) { Fail 'A configured storage directory is an existing file.' }
        AssertRestrictedAcl $Path $false
        foreach ($item in Get-ChildItem -LiteralPath $Path -Recurse -Force) {
            AssertRestrictedAcl $item.FullName $false
            if ((Get-Acl -LiteralPath $item.FullName).AreAccessRulesProtected -and $ManagedPaths -notcontains (FullPath $item.FullName)) {
                Fail 'Private storage contains protected descendant ACLs. Review their pool access and inheritance before deployment.'
            }
        }
    }
}
function AssertPublishTree([string]$Path, [Security.Principal.SecurityIdentifier]$PoolSid) {
    AssertNoLinks $Path -Descendants
    $broad = @('S-1-1-0','S-1-2-0','S-1-2-1','S-1-5-4','S-1-5-7','S-1-5-11','S-1-5-32-545','S-1-5-32-546','S-1-5-32-568')
    $write = [Security.AccessControl.FileSystemRights]::WriteData -bor [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($item in @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force)) {
        $acl = Get-Acl -LiteralPath $item.FullName
        foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -ne 'Allow' -or -not ($rule.FileSystemRights -band $write)) { continue }
            if ($broad -contains $rule.IdentityReference.Value) { Fail 'The published site has broad write access. Restrict its existing ACLs before deployment.' }
            if ($null -ne $PoolSid -and $rule.IdentityReference -eq $PoolSid -and $item.FullName -ine $Path -and
                (-not $rule.IsInherited -or $acl.AreAccessRulesProtected)) { Fail 'Published descendants grant the pool write access. Review their ACLs before deployment.' }
        }
    }
}
function AssertCertificateParent([string]$Path) {
    $broad = @('S-1-1-0','S-1-2-0','S-1-2-1','S-1-5-4','S-1-5-7','S-1-5-11','S-1-5-32-545','S-1-5-32-546','S-1-5-32-568')
    $write = [Security.AccessControl.FileSystemRights]::WriteData -bor [Security.AccessControl.FileSystemRights]::AppendData -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($rule in (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and ($rule.FileSystemRights -band $write) -and
            ($broad -contains $rule.IdentityReference.Value -or ($null -ne $poolSid -and $rule.IdentityReference -eq $poolSid))) {
            Fail 'A certificate directory permits broad or pool write access. Restrict that directory before deployment.'
        }
    }
}
function SetScopedAcl([string]$Path, [string]$Rights, [bool]$Directory, [bool]$NewDirectory = $false) {
    if ($NewDirectory) {
        $acl = [Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true, $false)
    }
    else {
        $acl = Get-Acl -LiteralPath $Path
        # Retain specific operator, backup and service grants, including inherited grants.
        $acl.SetAccessRuleProtection($true, $true)
    }
    $inheritance = if ($Directory) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    if ($NewDirectory) {
        foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($sid), 'FullControl', $inheritance, 'None', 'Allow'))
        }
    }
    $acl.SetAccessRule([Security.AccessControl.FileSystemAccessRule]::new("IIS AppPool\$PoolName", $Rights, $inheritance, 'None', 'Allow'))
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function CertificateDnsNames($Certificate) {
    $extension = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' })
    if (-not $extension.Count) { return @($Certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false)) }
    $bytes = $extension[0].RawData
    # Read DER dNSName entries without relying on localized certificate display text.
    $offset = 0
    function DerLength([byte[]]$Data, [ref]$Position) {
        if ($Position.Value -ge $Data.Length) { Fail 'The TLS certificate has invalid DNS-name data.' }
        $length = [int]$Data[$Position.Value]; $Position.Value++
        if ($length -band 128) {
            $count = $length -band 127
            if ($count -lt 1 -or $count -gt 4 -or $Position.Value + $count -gt $Data.Length) { Fail 'The TLS certificate has invalid DNS-name data.' }
            $length = 0
            for ($i = 0; $i -lt $count; $i++) { $length = $length * 256 + $Data[$Position.Value]; $Position.Value++ }
        }
        return $length
    }
    if ($bytes.Length -lt 2 -or $bytes[$offset++] -ne 48) { Fail 'The TLS certificate has invalid DNS-name data.' }
    $sequenceLength = DerLength $bytes ([ref]$offset)
    if ($offset + $sequenceLength -ne $bytes.Length) { Fail 'The TLS certificate has invalid DNS-name data.' }
    $names = @()
    while ($offset -lt $bytes.Length) {
        $tag = $bytes[$offset++]
        $length = DerLength $bytes ([ref]$offset)
        if ($length -lt 0 -or $offset + $length -gt $bytes.Length) { Fail 'The TLS certificate has invalid DNS-name data.' }
        if ($tag -eq 130) { $names += [Text.Encoding]::ASCII.GetString($bytes, $offset, $length) }
        $offset += $length
    }
    return $names
}
function AssertCertificateHost($Certificate, [string]$HostName) {
    $matches = @(CertificateDnsNames $Certificate | Where-Object {
        $_ -ieq $HostName -or ($_.StartsWith('*.') -and $HostName.Contains('.') -and $_.Substring(2) -ieq $HostName.Substring($HostName.IndexOf('.') + 1))
    })
    if (-not $matches.Count) { Fail 'The TLS certificate does not cover HostName.' }
}

$phase = 'preflight'
$mutationStarted = $false
$poolExists = $false
try {
    if (-not [Environment]::Is64BitOperatingSystem -or -not [Environment]::Is64BitProcess) { Fail 'Run this helper in 64-bit Windows PowerShell on a 64-bit IIS server.' }
    Import-Module WebAdministration
    $publish = FullPath $PublishPath
    if ($publish -eq [IO.Path]::GetPathRoot($publish).TrimEnd('\')) { Fail 'PublishPath must be a dedicated site directory, not a drive or share root.' }
    $privateRoot = FullPath $StorageRoot
    $settingsPath = FullPath $SettingsFile
    $poolPath = "IIS:\AppPools\$PoolName"
    $poolExists = Test-Path -LiteralPath $poolPath
    $existing = @{}
    $poolSid = $null
    $filter = "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables"
    if ($poolExists) {
        $pool = Get-Item -LiteralPath $poolPath
        if ([string]$pool.processModel.identityType -ne 'ApplicationPoolIdentity') { Fail 'The existing pool uses another Windows identity. Preserve that identity and its keys; review deployment manually.' }
        $poolSid = [Security.Principal.NTAccount]::new("IIS AppPool\$PoolName").Translate([Security.Principal.SecurityIdentifier])
        foreach ($entry in Get-WebConfiguration -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter "$filter/add") { $existing[[string]$entry.name] = [string]$entry.value }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'DrmcPatientPortal.dll') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $publish 'DrmcPatientPortal.exe') -PathType Leaf) -or -not (Test-Path -LiteralPath (Join-Path $publish 'web.config') -PathType Leaf)) { Fail 'PublishPath must contain the framework-dependent portal DLL, Windows apphost and web.config.' }
    if ($privateRoot -eq [IO.Path]::GetPathRoot($privateRoot).TrimEnd('\')) { Fail 'StorageRoot must be a dedicated directory, not a drive or share root.' }
    AssertSeparate $privateRoot $publish
    if ((IsWithin $settingsPath $publish) -or (IsWithin $settingsPath $privateRoot)) { Fail 'Keep SettingsFile outside the published site and mutable storage.' }
    AssertPublishTree $publish $poolSid
    AssertNoLinks $settingsPath
    AssertRestrictedAcl $settingsPath $true $false
    $settings = ReadSettings $settingsPath

    # Reconcile only this finite application-owned manifest; unrelated pool variables survive.
    $owned = @(
        'ASPNETCORE_ENVIRONMENT','DOTNET_ENVIRONMENT','ConnectionStrings__DefaultConnection','AllowedHosts','Portal__PublicOrigin',
        'DevelopmentFixtures__Enabled','AdminBootstrap__Enabled','AdminBootstrap__Email','Identity__RequireConfirmedAccount',
        'DataProtection__KeyRingPath','DataProtection__ProtectKeysWithDpapi','DataProtection__CertificatePath','DataProtection__CertificatePasswordFile',
        'PatientDocuments__RootPath','PatientDocuments__TemporaryPath','PatientDocuments__MaximumFileSizeMb','PatientDocuments__TemporaryFileLifetimeMinutes',
        'LabReports__RootPath','RadiologyReports__RootPath','RedStarLabReports__RootPath','AdvisoryImages__RootPath','Imports__StagingPath',
        'ResultStaging__Enabled','ResultStaging__LaboratoryPath','ResultStaging__RadiologyPath','ResultStaging__QuarantinePath','ResultStaging__PollIntervalSeconds',
        'Ocr__NativeLibraryPath','Ocr__TessDataPath','Ocr__MaxConcurrency','Ocr__MaxQueuedJobs','Ocr__QueueWaitTimeout','Ocr__ExtractionTimeout',
        'PortalOperations__MonitorWorkers','PortalOperations__MinimumFreeDiskMb','PortalOperations__QueueWarningMinutes',
        'PrivateRetention__Enabled','PrivateRetention__ApprovedPolicyId','PrivateRetention__OrphanGraceDays','WebsiteUsage__Enabled','WebsiteUsage__RetentionMonths',
        'RedStar__WorkerEnabled','RedStar__RemindersEnabled','RedStar__MaximumAttempts','AccountPrivacy__MinimumResponseMilliseconds',
        'AccountEmail__MaximumAttempts','AccountEmail__LifetimeMinutes','AccountEmail__RetentionDays','AccountEmail__WorkerEnabled',
        'Notifications__Email__Provider','Notifications__Email__Host','Notifications__Email__Port','Notifications__Email__UseSsl','Notifications__Email__Username',
        'Notifications__Email__Password','Notifications__Email__FromAddress','Notifications__Email__FromName','Notifications__Sms__Enabled',
        'Notifications__Sms__Endpoint','Notifications__Sms__ApiKey','Notifications__Sms__SenderId','Notifications__Outbox__WorkerEnabled',
        'Notifications__Outbox__MaximumAttempts','Notifications__Outbox__LifetimeMinutes','Notifications__Outbox__RetentionDays',
        'RateLimits__AuthenticationPerIp','RateLimits__AuthenticationPerAccount','RateLimits__EmailPerIp','RateLimits__EmailPerAccount',
        'RateLimits__ScansPerIp','RateLimits__ScansPerUser','RateLimits__ConcurrentScans','RateLimits__PdfPerUser',
        'PatientResults__ShowFullResults','PatientRegistration__CodeLifetimeDays','StaffInvitations__LifetimeDays'
    )
    foreach ($index in 0..31) {
        $owned += "Proxy__KnownProxies__$index"
        $owned += "DataProtection__DecryptionCertificates__${index}__Path", "DataProtection__DecryptionCertificates__${index}__PasswordFile"
    }
    foreach ($key in $settings.Keys) {
        if (($key.StartsWith('DataProtection__DecryptionCertificates__', [StringComparison]::OrdinalIgnoreCase) -or $key.StartsWith('Proxy__KnownProxies__', [StringComparison]::OrdinalIgnoreCase)) -and $owned -notcontains $key) { Fail 'Indexed certificate/proxy settings support indices 0 through 31 only.' }
    }
    # Older helpers could leave higher array indices. Remove only exact known property shapes.
    $owned += @($existing.Keys | Where-Object { $_ -match '^(?:Proxy__KnownProxies__\d+|DataProtection__DecryptionCertificates__\d+__(?:Path|PasswordFile))$' })
    foreach ($required in @('ConnectionStrings__DefaultConnection','AllowedHosts','Portal__PublicOrigin','Notifications__Email__Host','Notifications__Email__FromAddress','Notifications__Email__Username','Notifications__Email__Password')) {
        if ([string]::IsNullOrWhiteSpace($settings[$required])) { Fail "Missing required deployment setting: $required" }
    }
    try { $sql = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($settings['ConnectionStrings__DefaultConnection']) }
    catch { Fail 'ConnectionStrings__DefaultConnection must be a valid SQL Server connection string.' }
    if ([string]::IsNullOrWhiteSpace($sql.DataSource) -or [string]::IsNullOrWhiteSpace($sql.InitialCatalog) -or $sql.DataSource -match 'CONFIGURE_VIA_ENVIRONMENT' -or $sql.InitialCatalog -match 'CONFIGURE_VIA_ENVIRONMENT') { Fail 'ConnectionStrings__DefaultConnection requires an explicit SQL Server and database.' }
    if ([Uri]::CheckHostName($HostName) -ne [UriHostNameType]::Dns -or $HostName.EndsWith('.')) { Fail 'HostName must be a DNS hostname without a trailing dot.' }
    $origin = $null
    if (-not [Uri]::TryCreate($settings['Portal__PublicOrigin'], [UriKind]::Absolute, [ref]$origin) -or $origin.Scheme -ne 'https' -or $origin.Host -ine $HostName -or $origin.Port -ne 443 -or $origin.AbsolutePath -ne '/' -or $origin.UserInfo -or $origin.Query -or $origin.Fragment) { Fail 'Portal__PublicOrigin must be this site HTTPS origin on port 443.' }
    $hosts = @($settings['AllowedHosts'].Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($hosts -notcontains $HostName -or @($hosts | Where-Object { $_.Contains('*') -or [Uri]::CheckHostName($_) -eq [UriHostNameType]::Unknown }).Count) { Fail 'AllowedHosts must explicitly contain HostName and valid hosts, without wildcards or ports.' }
    if ($settings.ContainsKey('Notifications__Email__Provider') -and $settings['Notifications__Email__Provider'].Trim() -ine 'Smtp') { Fail 'Production email delivery must use Smtp.' }
    $null = IntegerSetting $settings 'Notifications__Email__Port' 587 1 65535
    try { $null = [Net.Mail.MailAddress]::new($settings['Notifications__Email__FromAddress']) } catch { Fail 'Notifications__Email__FromAddress must be a valid sender address.' }
    if ($settings.ContainsKey('Notifications__Email__FromName') -and $settings['Notifications__Email__FromName'] -match '[\r\n\u0085\u2028\u2029]') { Fail 'Notifications__Email__FromName must be a single line.' }
    if (-not (BooleanSetting $settings 'Notifications__Email__UseSsl' $true)) { Fail 'Production SMTP requires TLS.' }
    if (BooleanSetting $settings 'Notifications__Sms__Enabled' $false) {
        $sms = $null
        if (-not [Uri]::TryCreate($settings['Notifications__Sms__Endpoint'], [UriKind]::Absolute, [ref]$sms) -or $sms.Scheme -ne 'https' -or $sms.UserInfo -or [string]::IsNullOrWhiteSpace($settings['Notifications__Sms__ApiKey'])) { Fail 'Enabled SMS requires an HTTPS endpoint without URL credentials and an API key.' }
    }
    if (BooleanSetting $settings 'AdminBootstrap__Enabled' $false) {
        try { $bootstrapEmail = [Net.Mail.MailAddress]::new($settings['AdminBootstrap__Email']) } catch { Fail 'Enabled Admin bootstrap requires AdminBootstrap__Email.' }
        if ($bootstrapEmail.Address -ine $settings['AdminBootstrap__Email'].Trim()) { Fail 'AdminBootstrap__Email must be a bare email address.' }
        $settings['AdminBootstrap__Email'] = $bootstrapEmail.Address
    }
    $settings['AdminBootstrap__Enabled'] = (BooleanSetting $settings 'AdminBootstrap__Enabled' $false).ToString().ToLowerInvariant()
    foreach ($pair in @(@('ASPNETCORE_ENVIRONMENT','Production'),@('DOTNET_ENVIRONMENT','Production'),@('DevelopmentFixtures__Enabled','false'),@('Identity__RequireConfirmedAccount','true'),@('Notifications__Email__UseSsl','true'),@('PortalOperations__MonitorWorkers','true'))) {
        if ($settings.ContainsKey($pair[0]) -and $settings[$pair[0]] -ine $pair[1]) { Fail "Unsafe production setting: $($pair[0])" }
        $settings[$pair[0]] = $pair[1]
    }

    $directoryDefaults = [ordered]@{
        DataProtection__KeyRingPath='keys'; PatientDocuments__RootPath='patient-documents'; PatientDocuments__TemporaryPath='temporary-uploads'
        LabReports__RootPath='lab-reports'; RadiologyReports__RootPath='radiology-reports'; RedStarLabReports__RootPath='redstar-lab-reports'
        AdvisoryImages__RootPath='advisory-images'; Imports__StagingPath='imports'
    }
    if ((BooleanSetting $settings 'ResultStaging__Enabled' $false) -or
        @('ResultStaging__LaboratoryPath','ResultStaging__RadiologyPath','ResultStaging__QuarantinePath' | Where-Object { $settings.ContainsKey($_) -or $existing.ContainsKey($_) }).Count) {
        $directoryDefaults['ResultStaging__LaboratoryPath']='staging-laboratory'
        $directoryDefaults['ResultStaging__RadiologyPath']='staging-radiology'
        $directoryDefaults['ResultStaging__QuarantinePath']='staging-quarantine'
    }
    $null = IntegerSetting $settings 'ResultStaging__PollIntervalSeconds' 30 1 3600
    $directories = @{}
    foreach ($key in $directoryDefaults.Keys) {
        $configured = if ($settings.ContainsKey($key)) { $settings[$key] } elseif ($existing.ContainsKey($key)) { $existing[$key] } else { Join-Path $privateRoot $directoryDefaults[$key] }
        $path = FullPath $configured
        AssertSeparate $path $publish
        if ($path -eq [IO.Path]::GetPathRoot($path).TrimEnd('\')) { Fail 'Storage directories must not be drive or share roots.' }
        if ($existing.ContainsKey($key) -and (FullPath $existing[$key]) -ine $path -and -not $AllowStoragePathChange) { Fail "Changing $key requires -AllowStoragePathChange and a verified offline data/key migration." }
        foreach ($other in $directories.Values) { AssertSeparate $path $other }
        $directories[$key] = $path; $settings[$key] = $path
    }
    foreach ($path in @($privateRoot) + @($directories.Values)) { AssertPrivateTree $path -ManagedPaths @($directories.Values) }
    foreach ($path in $directories.Values) { if (IsWithin $settingsPath $path) { Fail 'Keep SettingsFile outside mutable storage directories.' } }

    foreach ($key in @('DataProtection__ProtectKeysWithDpapi','DataProtection__CertificatePath','DataProtection__CertificatePasswordFile')) {
        if (-not $settings.ContainsKey($key) -and $existing.ContainsKey($key)) { $settings[$key] = $existing[$key] }
    }
    $dpapi = BooleanSetting $settings 'DataProtection__ProtectKeysWithDpapi' $false
    $useCertificate = -not [string]::IsNullOrWhiteSpace($settings['DataProtection__CertificatePath'])
    if ($dpapi -eq $useCertificate) { Fail 'Choose exactly one of DataProtection__ProtectKeysWithDpapi or DataProtection__CertificatePath.' }
    if ($existing.ContainsKey('DataProtection__ProtectKeysWithDpapi') -or $existing.ContainsKey('DataProtection__CertificatePath')) {
        $oldDpapi = BooleanSetting $existing 'DataProtection__ProtectKeysWithDpapi' $false
        if ($dpapi -ne $oldDpapi -or ($useCertificate -and (FullPath $existing['DataProtection__CertificatePath']) -ine (FullPath $settings['DataProtection__CertificatePath']))) { Fail 'The existing key-protection mode or active certificate cannot be changed by this helper. Complete an offline protection migration first.' }
    }
    $secretFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $certificatePaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($index in @(-1) + @(0..31)) {
        $pathKey = if ($index -eq -1) { 'DataProtection__CertificatePath' } else { "DataProtection__DecryptionCertificates__${index}__Path" }
        $passwordKey = if ($index -eq -1) { 'DataProtection__CertificatePasswordFile' } else { "DataProtection__DecryptionCertificates__${index}__PasswordFile" }
        if ([string]::IsNullOrWhiteSpace($settings[$pathKey])) { if ($settings.ContainsKey($passwordKey)) { Fail 'A certificate password file requires its corresponding certificate path.' }; continue }
        if (-not $useCertificate) { Fail 'Historical certificates require certificate key protection.' }
        $certPath = FullPath $settings[$pathKey]
        $settings[$pathKey] = $certPath
        $null = $certificatePaths.Add($certPath); $null = $secretFiles.Add($certPath)
        $password = $null; $passwordPath = $null
        if ($settings.ContainsKey($passwordKey)) {
            $passwordPath = FullPath $settings[$passwordKey]; $settings[$passwordKey] = $passwordPath
            $null = $secretFiles.Add($passwordPath)
        }
        foreach ($file in @($certPath) + @($passwordPath | Where-Object { $_ })) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Fail 'A Data Protection certificate or password file is unavailable.' } }
        if ($passwordPath) { $password = [IO.File]::ReadAllText($passwordPath).TrimEnd([char[]]@([char]13,[char]10)) }
        $protectionCertificate = $null
        try {
            $protectionCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certPath, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
            if (-not $protectionCertificate.HasPrivateKey) { Fail 'Data Protection certificates must contain an RSA private key.' }
            $rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($protectionCertificate)
            if ($null -eq $rsa) { Fail 'Data Protection certificates must contain an RSA private key.' }
            $rsa.Dispose()
        }
        catch { Fail 'A Data Protection RSA certificate could not be opened. Check its path, password file and private key.' }
        finally { if ($null -ne $protectionCertificate) { $protectionCertificate.Dispose() }; $password = $null }
    }
    if (Test-Path -LiteralPath $directories['DataProtection__KeyRingPath'] -PathType Container) {
        $hasKeys = @(Get-ChildItem -LiteralPath $directories['DataProtection__KeyRingPath'] -Filter 'key-*.xml' -File).Count -gt 0
        if ($hasKeys) {
            foreach ($key in $existing.Keys) {
                if ($key -match '^DataProtection__DecryptionCertificates__\d+__Path$' -and -not [string]::IsNullOrWhiteSpace($existing[$key]) -and -not $certificatePaths.Contains((FullPath $existing[$key]))) { Fail 'Retain historical certificates while existing keys may require them. Verify an offline key migration before removing them.' }
            }
        }
    }
    foreach ($file in $secretFiles) {
        if (IsWithin $file $publish) { Fail 'Keep Data Protection certificates and password files outside the published site.' }
        foreach ($path in @($privateRoot) + @($directories.Values)) { if (IsWithin $file $path) { Fail 'Keep certificate/password files outside mutable storage.' } }
        AssertNoLinks $file; AssertRestrictedAcl $file $true
        AssertCertificateParent (Split-Path -Path $file -Parent)
    }
    if (-not (Get-WebGlobalModule -Name 'AspNetCoreModuleV2')) { Fail 'Install or repair the x64 .NET 10 Hosting Bundle after installing IIS.' }
    if (-not (Get-WebGlobalModule -Name 'ApplicationInitializationModule')) { Fail 'Install the IIS Application Initialization feature.' }
    $dotnetHost = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotnetHost -PathType Leaf)) { Fail 'Install the x64 .NET Hosting Bundle shared frameworks.' }
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $publish 'DrmcPatientPortal.runtimeconfig.json') -Raw | ConvertFrom-Json
    $runtimes = @(& $dotnetHost --list-runtimes 2>&1)
    if ($LASTEXITCODE -ne 0) { Fail 'The x64 .NET host could not enumerate installed shared frameworks.' }
    foreach ($frameworkName in @('Microsoft.NETCore.App','Microsoft.AspNetCore.App')) {
        $required = @($runtimeConfig.runtimeOptions.frameworks | Where-Object { $_.name -eq $frameworkName })
        if ($required.Count -ne 1 -or $required[0].version -notmatch '^\d+\.\d+\.\d+$') { Fail 'PublishPath must contain a supported framework-dependent runtime configuration.' }
        $version = [version]$required[0].version
        $available = @($runtimes | ForEach-Object { if ($_ -match ('^' + [regex]::Escape($frameworkName) + ' (\d+\.\d+\.\d+) \[')) { [version]$Matches[1] } } | Where-Object { $_.Major -eq $version.Major -and $_.Minor -eq $version.Minor -and $_ -ge $version -and $_ -ge [version]'10.0.14' })
        if ($version.Major -ne 10 -or $version.Minor -ne 0 -or -not $available.Count) { Fail 'Install the x64 .NET 10 Hosting Bundle 10.0.14 or later supported 10.0 security patch for both shared frameworks.' }
    }
    [xml]$webConfig = Get-Content -LiteralPath (Join-Path $publish 'web.config') -Raw
    $ancm = $webConfig.SelectSingleNode('//system.webServer/aspNetCore')
    if ($null -eq $ancm -or $ancm.hostingModel -ine 'inprocess') { Fail 'The published web.config must use the InProcess IIS hosting model.' }
    if (@($webConfig.SelectNodes('//aspNetCore/environmentVariables/environmentVariable')).Count) { Fail 'Configure environment settings at the dedicated pool, not in the published web.config.' }
    $certificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$CertificateThumbprint"
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { Fail 'The TLS certificate must have a private key and be within its validity period.' }
    AssertCertificateHost $certificate $HostName
    $eku = @($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' })
    if ($eku.Count -and @($eku[0].EnhancedKeyUsages | Where-Object { $_.Value -in @('1.3.6.1.5.5.7.3.1','2.5.29.37.0') }).Count -eq 0) { Fail 'The TLS certificate must permit server authentication.' }

    $siteExists = Test-Path -LiteralPath "IIS:\Sites\$SiteName"
    $bindingInformation = "*:443:$HostName"
    foreach ($site in Get-Website) {
        if ($site.Name -ne $SiteName -and $site.applicationPool -eq $PoolName) { Fail 'Use a dedicated pool; another IIS site is already assigned to PoolName.' }
        if ($site.Name -ne $SiteName -and @(Get-WebBinding -Name $site.Name | Where-Object { $_.protocol -eq 'https' -and $_.bindingInformation -match (':443:' + [regex]::Escape($HostName) + '$') }).Count) { Fail 'Another IIS site already has the requested HTTPS hostname binding.' }
        foreach ($application in Get-WebApplication -Site $site.Name) { if ($application.applicationPool -eq $PoolName) { Fail 'Use a dedicated pool; a nested IIS application already uses PoolName.' } }
    }
    if ($siteExists) {
        $site = Get-Item -LiteralPath "IIS:\Sites\$SiteName"
        if ((FullPath $site.physicalPath) -ine $publish -or $site.applicationPool -ne $PoolName) { Fail 'An existing site uses another publish directory or pool. Review that deployment before rebinding it.' }
        $bindings = @(Get-WebBinding -Name $SiteName)
        if ($bindings.Count -ne 1 -or $bindings[0].protocol -ne 'https' -or $bindings[0].bindingInformation -ine $bindingInformation) { Fail 'The existing site must have exactly the intended HTTPS hostname binding on port 443.' }
        if (@(Get-WebApplication -Site $SiteName).Count) { Fail 'The dedicated portal site must not contain nested IIS applications.' }
    }
    $manager = [Microsoft.Web.Administration.ServerManager]::new()
    try { $null = @($manager.WorkerProcesses) } finally { $manager.Dispose() }
    if (-not $PSCmdlet.ShouldProcess("IIS site $SiteName and dedicated pool $PoolName", 'Apply validated production settings and scoped ACLs; leave the pool stopped')) { return }

    $phase = 'configuration'; $mutationStarted = $true
    if (-not $poolExists) {
        New-WebAppPool -Name $PoolName | Out-Null; $poolExists = $true
        Set-ItemProperty -LiteralPath $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    }
    if ((Get-WebAppPoolState -Name $PoolName).Value -ne 'Stopped') { Stop-WebAppPool -Name $PoolName }
    $deadline = (Get-Date).AddSeconds(100)
    do {
        $state = (Get-WebAppPoolState -Name $PoolName).Value
        $manager = [Microsoft.Web.Administration.ServerManager]::new()
        try { $workers = @($manager.WorkerProcesses | Where-Object { $_.AppPoolName -eq $PoolName }) } finally { $manager.Dispose() }
        if ($state -eq 'Stopped' -and -not $workers.Count) { break }
        if ((Get-Date) -ge $deadline) { Fail 'The target pool did not stop within 100 seconds. No files or settings were changed; inspect its remaining workers.' }
        Start-Sleep -Milliseconds 250
    } while ($true)
    foreach ($property in @(
        @('managedRuntimeVersion',''),@('enable32BitAppOnWin64',$false),@('startMode','AlwaysRunning'),@('autoStart',$true),
        @('processModel.loadUserProfile',$true),@('processModel.maxProcesses',1),@('processModel.idleTimeout',[TimeSpan]::Zero),
        @('processModel.shutdownTimeLimit',[TimeSpan]::FromSeconds(90)),@('recycling.disallowOverlappingRotation',$true),@('recycling.periodicRestart.time',[TimeSpan]::Zero)
    )) { Set-ItemProperty -LiteralPath $poolPath -Name $property[0] -Value $property[1] }
    foreach ($path in @($privateRoot) + @($directories.Values | Select-Object -Unique)) {
        $isNew = -not (Test-Path -LiteralPath $path)
        if ($isNew) { New-Item -ItemType Directory -Path $path -Force | Out-Null }
        SetScopedAcl $path 'Modify' $true $isNew
    }
    SetScopedAcl $publish 'ReadAndExecute' $true
    foreach ($file in $secretFiles) {
        SetScopedAcl $file 'Read' $false
        # Non-inheriting traversal permission never grants writes or access to other secret files.
        $parent = Split-Path -Path $file -Parent
        $acl = Get-Acl -LiteralPath $parent
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new("IIS AppPool\$PoolName", 'ReadAndExecute', 'None', 'None', 'Allow'))
        Set-Acl -LiteralPath $parent -AclObject $acl
    }
    foreach ($key in $owned) { if ($existing.ContainsKey($key) -and -not $settings.ContainsKey($key)) { Remove-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -AtElement @{name=$key} } }
    foreach ($key in $settings.Keys) {
        $itemFilter = "$filter/add[@name='$key']"
        if ($existing.ContainsKey($key)) { Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $itemFilter -Name value -Value $settings[$key] }
        else { Add-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Filter $filter -Name '.' -Value @{name=$key;value=$settings[$key]} }
    }
    if (-not $siteExists) { New-Website -Name $SiteName -PhysicalPath $publish -ApplicationPool $PoolName -Port 443 -HostHeader $HostName -Ssl -SslFlags 1 | Out-Null }
    Set-ItemProperty -LiteralPath "IIS:\Sites\$SiteName" -Name serverAutoStart -Value $true
    Set-ItemProperty -LiteralPath "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true
    Set-WebBinding -Name $SiteName -BindingInformation $bindingInformation -PropertyName sslFlags -Value 1
    (Get-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName).AddSslCertificate($CertificateThumbprint, 'My')
    Set-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST' -Location $SiteName -Filter 'system.webServer/applicationInitialization' -Name doAppInitAfterRestart -Value $true
    Write-Output 'IIS configuration applied. Pool remains stopped. Complete offline protection maintenance with the same database, storage and keys, then start the pool and verify readiness.'
}
catch {
    # Provider/native exceptions may contain supplied values. Emit controlled messages only.
    if ($_.Exception.Data['PortalDeploymentSafeMessage']) { throw [InvalidOperationException]::new($_.Exception.Message) }
    throw [InvalidOperationException]::new("IIS deployment $phase failed. Inspect server prerequisites, permissions and configuration without printing secret settings.")
}
finally {
    if ($mutationStarted -and $poolExists) {
        try { if ((Get-WebAppPoolState -Name $PoolName).Value -ne 'Stopped') { Stop-WebAppPool -Name $PoolName } }
        catch { Write-Warning 'The target pool could not be confirmed stopped. Stop that pool before continuing deployment.' }
    }
}
