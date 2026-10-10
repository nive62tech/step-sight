param(
    [Parameter(Mandatory)][string]$CommandsPath,
    [string]$InstanceId = 'i-09b7452f878f98708',
    [string]$Profile = 'personal',
    [string]$Region = 'ap-south-1'
)
. "$PSScriptRoot/common.ps1"
$parameters = Get-Content -LiteralPath $CommandsPath -Raw | ConvertFrom-Json
if ($parameters -is [string] -or -not $parameters.commands -or
    @($parameters.commands | Where-Object { $_ -isnot [string] }).Count) { throw 'SSM parameters must be a JSON object with a commands array' }
$identity = aws sts get-caller-identity --profile $Profile --output json | ConvertFrom-Json
Assert-NativeSuccess 'AWS identity check'
if ($identity.Account -ne '214422569127' -or $Region -ne 'ap-south-1') { throw 'Outside approved pilot scope' }
$instance = aws ec2 describe-instances --instance-ids $InstanceId --profile $Profile --region $Region --output json | ConvertFrom-Json
Assert-NativeSuccess 'Pilot instance check'
$tags = $instance.Reservations[0].Instances[0].Tags
if (-not ($tags | Where-Object { $_.Key -eq 'Project' -and $_.Value -eq 'DesktopGuide' }) -or
    -not ($tags | Where-Object { $_.Key -eq 'Environment' -and $_.Value -eq 'pilot' })) { throw 'Target is not the isolated pilot' }
$response = aws ssm send-command --instance-ids $InstanceId --document-name AWS-RunShellScript --parameters "file://$((Resolve-Path -LiteralPath $CommandsPath).Path)" --timeout-seconds 1800 --profile $Profile --region $Region --output json | ConvertFrom-Json
Assert-NativeSuccess 'Pilot SSM command submission'
# Print only the command identifier, never its parameters or secrets.
$response.Command.CommandId
