
# nightly-push.ps1 — auto-commit and push any changes to origin/nightly
# Registered as a Windows Scheduled Task; runs daily at 02:00.

Set-StrictMode -Off
$ErrorActionPreference = 'Stop'

$repo = "C:\Users\Naabin\Documents\Timberborn\Mods\HTTPAutomation"
Set-Location $repo

# Only commit if there is something staged or modified
$status = git status --porcelain 2>&1
if (-not $status) {
    Write-Output "$(Get-Date -f 'yyyy-MM-dd HH:mm') nightly-push: nothing to commit, skipping."
    exit 0
}

$date = Get-Date -Format "yyyy-MM-dd"
git add -A
git commit -m "nightly auto-commit $date"
git push origin HEAD:Nightly

Write-Output "$(Get-Date -f 'yyyy-MM-dd HH:mm') nightly-push: pushed OK."
