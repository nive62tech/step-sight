terraform {

  required_version = "~> 1.16.0"
  required_providers {
    google = {
      source = "hashicorp/google", version = "~> 7.0"
    }

  }

  backend "s3" {

  }


}

variable "project_id" {
  type    = string
  default = "dg-pilot-214422569127"
}

variable "billing_account" {
  type    = string
  default = ""
}

variable "create_project" {
  type    = bool
  default = true
}

variable "deployment_quota_project" {
  type    = string
  default = ""
}

variable "aws_account_id" {
  type    = string
  default = "214422569127"
}

variable "aws_role_name" {
  type    = string
  default = "dg-pilot-runtime"
}

variable "enable_speech" {
  type    = bool
  default = false
}

provider "google" {
  project               = var.project_id
  billing_project       = var.create_project ? (var.deployment_quota_project == "" ? null : var.deployment_quota_project) : var.project_id
  user_project_override = !var.create_project || var.deployment_quota_project != ""
}

resource "google_project" "pilot" {
  count           = var.create_project ? 1 : 0
  project_id      = var.project_id
  name            = "Desktop Guide Pilot"
  billing_account = var.billing_account
  deletion_policy = "PREVENT"
  lifecycle {
    prevent_destroy = true
  }


}

# Existing projects are read only. Their billing and lifecycle remain unmanaged.
data "google_project" "existing" {
  count      = var.create_project ? 0 : 1
  project_id = var.project_id
}

locals {
  project_id = var.create_project ? google_project.pilot[0].project_id : data.google_project.existing[0].project_id
}

resource "google_project_service" "apis" {

  for_each           = toset(concat(["aiplatform.googleapis.com", "iam.googleapis.com", "iamcredentials.googleapis.com", "sts.googleapis.com", "serviceusage.googleapis.com"], var.enable_speech ? ["texttospeech.googleapis.com"] : []))
  project            = local.project_id
  service            = each.value
  disable_on_destroy = false

}

resource "google_service_account" "runtime" {

  project      = local.project_id
  account_id   = "desktop-guide-runtime"
  display_name = "Desktop Guide AWS inference"
  depends_on   = [google_project_service.apis]

}

resource "google_project_iam_custom_role" "inference" {

  project     = local.project_id
  role_id     = "desktopGuideInference"
  title       = "Desktop Guide inference only"
  permissions = concat(["aiplatform.endpoints.predict", "serviceusage.services.use"], var.enable_speech ? ["texttospeech.synthesize", "texttospeech.voices.list"] : [])
  depends_on  = [google_project_service.apis]

}

resource "google_project_iam_member" "inference" {

  project = local.project_id
  role    = google_project_iam_custom_role.inference.name
  member  = "serviceAccount:${google_service_account.runtime.email}"

}

resource "google_iam_workload_identity_pool" "aws" {

  project                   = local.project_id
  workload_identity_pool_id = "desktop-guide-aws"
  display_name              = "Desktop Guide AWS runtime"
  depends_on                = [google_project_service.apis]

}

resource "google_iam_workload_identity_pool_provider" "aws" {

  project                            = local.project_id
  workload_identity_pool_id          = google_iam_workload_identity_pool.aws.workload_identity_pool_id
  workload_identity_pool_provider_id = "ec2-runtime"
  attribute_mapping = {

    "google.subject"        = "assertion.arn"
    "attribute.aws_account" = "assertion.account"
    "attribute.aws_role"    = "assertion.arn.contains('assumed-role') ? assertion.arn.extract('{account_arn}assumed-role/') + 'assumed-role/' + assertion.arn.extract('assumed-role/{role_name}/') : assertion.arn"

  }

  attribute_condition = "assertion.account == '${var.aws_account_id}' && attribute.aws_role == 'arn:aws:sts::${var.aws_account_id}:assumed-role/${var.aws_role_name}'"
  aws {
    account_id = var.aws_account_id
  }


}

resource "google_service_account_iam_member" "federation" {

  service_account_id = google_service_account.runtime.name
  role               = "roles/iam.workloadIdentityUser"
  member             = "principalSet://iam.googleapis.com/${google_iam_workload_identity_pool.aws.name}/attribute.aws_role/arn:aws:sts::${var.aws_account_id}:assumed-role/${var.aws_role_name}"

}

output "project_id" {
  value = local.project_id
}

output "runtime_service_account" {
  value = google_service_account.runtime.email
}

output "provider_resource" {
  value = google_iam_workload_identity_pool_provider.aws.name
}

output "external_account_config" {

  value = jsonencode({

    type                              = "external_account"
    audience                          = "//iam.googleapis.com/${google_iam_workload_identity_pool_provider.aws.name}"
    subject_token_type                = "urn:ietf:params:aws:token-type:aws4_request"
    token_url                         = "https://sts.googleapis.com/v1/token"
    service_account_impersonation_url = "https://iamcredentials.googleapis.com/v1/projects/-/serviceAccounts/${google_service_account.runtime.email}:generateAccessToken"
    credential_source = {

      environment_id                 = "aws1"
      region_url                     = "http://169.254.169.254/latest/meta-data/placement/availability-zone"
      url                            = "http://169.254.169.254/latest/meta-data/iam/security-credentials"
      regional_cred_verification_url = "https://sts.{region}.amazonaws.com?Action=GetCallerIdentity&Version=2011-06-15"
      imdsv2_session_token_url       = "http://169.254.169.254/latest/api/token"

    }


    }
  )

}
