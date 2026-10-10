param([string]$BillingAccount = '01ED8F-CD9E7C-454F36')
. "$PSScriptRoot/common.ps1"
# Local copies have local state so planning does not need to create the state bucket.
# No apply, imports, or cloud writes occur here. Plans may contain public resource identifiers.
$env:AWS_PROFILE = 'personal'
$env:AWS_REGION = 'ap-south-1'
$previewRoot = Join-Path $script:ProjectRoot ('.tools/infra-preview-' + [Guid]::NewGuid().ToString('N'))
$artifactRoot = Join-Path $script:ProjectRoot 'artifacts/cloud-plans'
New-Item -ItemType Directory -Force $artifactRoot | Out-Null
foreach ($root in @('aws/bootstrap', 'aws/pilot', 'gcp/inference')) {
    $sourcePath = Join-Path $script:ProjectRoot "infra/$root"
    $targetPath = Join-Path $previewRoot $root
    New-Item -ItemType Directory -Force $targetPath | Out-Null
    Get-ChildItem $sourcePath -File | Where-Object { $_.Name -eq 'main.tf' -or $_.Extension -eq '.sh' -or $_.Name -eq '.terraform.lock.hcl' } | Copy-Item -Destination $targetPath
    $installedProviders = Join-Path $sourcePath '.terraform/providers'
    if (Test-Path $installedProviders) {
        New-Item -ItemType Directory -Force (Join-Path $targetPath '.terraform') | Out-Null
        Copy-Item -LiteralPath $installedProviders -Destination (Join-Path $targetPath '.terraform/providers') -Recurse
    }
    $mainPath = Join-Path $targetPath 'main.tf'
    $text = Get-Content -LiteralPath $mainPath -Raw
    $text = [regex]::Replace($text, 'backend "s3"\s*\{\s*\}', '')
    [IO.File]::WriteAllText($mainPath, $text)
    & $script:Terraform "-chdir=$targetPath" init -input=false; Assert-NativeSuccess "Initialize preview $root"
    $planArguments = @("-chdir=$targetPath", 'plan', '-input=false', '-lock=false', '-out=preview.tfplan')
    if ($root -eq 'gcp/inference') { $planArguments += "-var=billing_account=$BillingAccount" }
    $name = $root.Replace('/', '-')
    & $script:Terraform @planArguments -no-color | Set-Content -Encoding UTF8 (Join-Path $artifactRoot "$name.plan.log")
    Assert-NativeSuccess "Read-only plan $root (see artifacts/cloud-plans/$name.plan.log)"
    & $script:Terraform "-chdir=$targetPath" show -no-color preview.tfplan | Set-Content -Encoding UTF8 (Join-Path $artifactRoot "$name.txt")
    & $script:Terraform "-chdir=$targetPath" show -json preview.tfplan | Set-Content -Encoding UTF8 (Join-Path $artifactRoot "$name.json")
    $planData = Get-Content (Join-Path $artifactRoot "$name.json") -Raw | ConvertFrom-Json
    $creates = @($planData.resource_changes | Where-Object { $_.change.actions -contains 'create' }).Count
    Write-Output "$name : $creates resources to create (review only)"
}
Write-Output "Review-only plans saved to $artifactRoot. Deployment requires separately approved plans against the real state backend."
