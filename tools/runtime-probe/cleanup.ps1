param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [string]$GameRoot='G:/SteamLibrary/steamapps/common/RimWorld',
    [switch]$WaitForExit
)
$ErrorActionPreference='Stop'
$receiptPath=Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Manifest))) 'cleanup.json'
try {
    $run=Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    $target=[IO.Path]::GetFullPath($run.mod)
    $probeProfilePath=[IO.Path]::GetFullPath($run.profile)
    $modsRoot=[IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods')).TrimEnd('\')
    $probeProfilePathsRoot=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'mapgen-ai-probe')).TrimEnd('\')+'\'
    if((Split-Path -Parent $target) -ne $modsRoot -or (Split-Path -Leaf $target) -notmatch '^MapGenAI_Probe_\d{8}-\d{6}$'){throw 'Refusing target outside the intended temporary mod directory'}
    if(-not $probeProfilePath.StartsWith($probeProfilePathsRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Profile outside owned probe directory'}
    if(-not (Test-Path -LiteralPath $target)){return}
    if((Get-Item -LiteralPath $target).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Refusing a linked mod directory'}
    $marker=Join-Path $target 'MAPGENAI_PROBE_OWNED'
    if(-not (Test-Path -LiteralPath $marker) -or -not (Test-Path -LiteralPath (Join-Path $probeProfilePath 'MAPGENAI_DISPOSABLE'))){throw 'Ownership markers missing'}
    if([IO.Path]::GetFullPath((Get-Content -LiteralPath $marker -Raw).Trim()) -ne $probeProfilePath){throw 'Ownership marker differs from launch manifest'}
    $ownedProcess=Get-CimInstance Win32_Process -Filter "ProcessId=$($run.pid)"
    if($ownedProcess -and $ownedProcess.Name -eq 'RimWorldWin64.exe' -and $ownedProcess.CommandLine.IndexOf($probeProfilePath,[StringComparison]::OrdinalIgnoreCase) -ge 0) {
        if(-not $WaitForExit){throw 'Probe still running; cleanup deferred'}
        $handle=Get-Process -Id $run.pid -ErrorAction SilentlyContinue
        if($handle -and -not $handle.WaitForExit(900000)){throw 'Probe did not exit within 15 minutes; cleanup deferred without terminating it'}
    }
    foreach($game in @(Get-CimInstance Win32_Process -Filter "Name='RimWorldWin64.exe'")) {
        if($game.CommandLine -and $game.CommandLine.IndexOf($probeProfilePath,[StringComparison]::OrdinalIgnoreCase) -ge 0){throw 'Owned profile is still running'}
    }
    Remove-Item -LiteralPath $target -Recurse -Force
    if(Test-Path -LiteralPath $target){throw 'Temporary mod directory remains'}
    @{removed=$true;mod=$target;profileRetained=$probeProfilePath;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
    Write-Output "Removed owned probe: $(Split-Path -Leaf $target)"
} catch {
    @{removed=$false;error=$_.Exception.Message;utc=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding utf8
    throw
}
