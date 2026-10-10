. "$PSScriptRoot/common.ps1"
Push-Location $script:ProjectRoot
try {
    & $script:Terraform fmt -check -recursive infra; Assert-NativeSuccess 'Terraform format'
    foreach ($root in @('infra/aws/bootstrap','infra/aws/pilot','infra/gcp/inference')) {
        & $script:Terraform "-chdir=$root" init -backend=false -input=false; Assert-NativeSuccess "Terraform init $root"
        & $script:Terraform "-chdir=$root" validate; Assert-NativeSuccess "Terraform validate $root"
    }
} finally { Pop-Location }
