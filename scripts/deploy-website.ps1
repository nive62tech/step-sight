param(
    [switch]$Apply,
    [string]$Profile = 'personal',
    [string]$Region = 'ap-south-1',
    [string]$ExpectedAccount = '214422569127',
    [string]$StackName = 'desktop-guide-website'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$siteRoot = Join-Path $projectRoot 'apps/website'
function Invoke-SiteAws {
    param([string[]]$Arguments)
    $result = & aws @Arguments --profile $Profile --region $Region --output json --no-cli-pager
    if ($LASTEXITCODE -ne 0) { throw "AWS command failed: $($Arguments[0..1] -join ' ')" }
    if ($result) { return ($result -join "`n" | ConvertFrom-Json) }
}
Push-Location $projectRoot
try {
    $identity = Invoke-SiteAws -Arguments @('sts','get-caller-identity')
    if ($identity.Account -ne $ExpectedAccount) { throw 'AWS account mismatch.' }
    & node --test "$siteRoot/tests/releases.test.mjs"
    if ($LASTEXITCODE -ne 0) { throw 'Release checks failed.' }
    & node "$siteRoot/scripts/build.mjs"
    if ($LASTEXITCODE -ne 0) { throw 'Website build failed.' }
    Invoke-SiteAws -Arguments @('cloudformation','validate-template','--template-body','file://infra/aws/website/stack.yaml') | Out-Null
    $deployArgs = @('cloudformation','deploy','--template-file','infra/aws/website/stack.yaml','--stack-name',$StackName,'--no-fail-on-empty-changeset','--tags','Application=desktop-guide-website')
    if (-not $Apply) { $deployArgs += '--no-execute-changeset' }
    & aws @deployArgs --profile $Profile --region $Region --no-cli-pager
    if ($LASTEXITCODE -ne 0) { throw 'Website infrastructure deployment failed.' }
    if (-not $Apply) { Write-Host 'Plan created without execution. Review the change set; use -Apply only within authorized deployment scope.'; return }
    $stack = (Invoke-SiteAws -Arguments @('cloudformation','describe-stacks','--stack-name',$StackName)).Stacks[0]
    $outputs = @{}
    foreach ($item in $stack.Outputs) { $outputs[$item.OutputKey] = $item.OutputValue }
    $bucket = $outputs.BucketName
    $distribution = $outputs.DistributionId
    if (-not $bucket -or -not $distribution) { throw 'Missing website stack outputs.' }
    $manifest = Get-Content "$siteRoot/releases.json" -Raw | ConvertFrom-Json
    # No --delete: historical downloads remain available across future site releases.
    # Refuse to replace an existing versioned binary with different content.
    foreach ($release in $manifest.releases) {
        $key = $release.url.TrimStart('/')
        $objects = Invoke-SiteAws -Arguments @('s3api','list-objects-v2','--bucket',$bucket,'--prefix',$key,'--max-keys','10')
        $existing = @($objects.Contents | Where-Object { $_.Key -eq $key })
        if ($existing.Count -gt 0) {
            $head = Invoke-SiteAws -Arguments @('s3api','head-object','--bucket',$bucket,'--key',$key)
            if ($head.Metadata.sha256 -ne $release.sha256 -or $head.ContentLength -ne $release.bytes) { throw "Immutable release collision: $key" }
        } else {
            $packagePath = Join-Path "$siteRoot/dist" $key
            & aws s3 cp $packagePath "s3://$bucket/$key" --profile $Profile --region $Region --content-type application/zip --content-disposition "attachment; filename=$([IO.Path]::GetFileName($key))" --cache-control 'public,max-age=31536000,immutable' --metadata "sha256=$($release.sha256)" --only-show-errors
            if ($LASTEXITCODE -ne 0) { throw 'Package upload failed.' }
        }
        & aws s3 cp (Join-Path "$siteRoot/dist" "$key.sha256") "s3://$bucket/$key.sha256" --profile $Profile --region $Region --content-type 'text/plain; charset=utf-8' --cache-control 'public,max-age=31536000,immutable' --only-show-errors
        if ($LASTEXITCODE -ne 0) { throw 'Checksum upload failed.' }
    }
    & aws s3 sync "$siteRoot/dist/" "s3://$bucket/" --profile $Profile --region $Region --exclude 'downloads/*' --cache-control 'public,max-age=300,must-revalidate' --only-show-errors
    if ($LASTEXITCODE -ne 0) { throw 'Website upload failed.' }
    $invalidation = Invoke-SiteAws -Arguments @('cloudfront','create-invalidation','--distribution-id',$distribution,'--paths','/','/index.html','/404.html','/releases.json','/assets/*')
    $receipt = [ordered]@{ WebsiteUrl=$outputs.WebsiteUrl; StackName=$StackName; BucketName=$bucket; DistributionId=$distribution; InvalidationId=$invalidation.Invalidation.Id; Release=$manifest.latest; DeployedAt=[DateTime]::UtcNow.ToString('o') }
    New-Item -ItemType Directory -Path "$projectRoot/artifacts" -Force | Out-Null
    $receipt | ConvertTo-Json | Set-Content "$projectRoot/artifacts/website-deployment.json"
    $receipt | ConvertTo-Json
} finally { Pop-Location }
