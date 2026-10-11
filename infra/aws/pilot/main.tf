terraform {

  required_version = "~> 1.16.0"
  required_providers {
    aws = {
      source = "hashicorp/aws", version = "~> 6.0"
    }

  }

  backend "s3" {

  }


}

provider "aws" {

  region              = var.region
  allowed_account_ids = [var.aws_account_id]
  default_tags {
    tags = {
      Project = "DesktopGuide", Environment = "pilot", ManagedBy = "Terraform"
    }

  }


}

variable "aws_account_id" {
  type    = string
  default = "214422569127"
}

variable "region" {
  type    = string
  default = "ap-south-1"
}

variable "instance_type" {
  type    = string
  default = "t3a.small"
}

variable "bootstrap_cloudfront_ingress" {
  type    = bool
  default = true
}

variable "budget_alert_email" {
  type    = string
  default = ""
}

variable "callback_url" {
  type    = string
  default = "http://localhost:43827/callback/"
}

variable "custom_domain" {
  type    = string
  default = ""
}

variable "acm_certificate_arn" {
  type    = string
  default = ""
}


data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_ssm_parameter" "ami" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-x86_64"
}

data "aws_ec2_managed_prefix_list" "cloudfront" {
  name = "com.amazonaws.global.cloudfront.origin-facing"
}


resource "aws_vpc" "pilot" {

  cidr_block           = "10.74.0.0/16"
  enable_dns_hostnames = true
  enable_dns_support   = true

}

resource "aws_subnet" "runtime" {

  vpc_id                  = aws_vpc.pilot.id
  cidr_block              = "10.74.1.0/24"
  availability_zone       = data.aws_availability_zones.available.names[0]
  map_public_ip_on_launch = true

}

resource "aws_internet_gateway" "egress" {
  vpc_id = aws_vpc.pilot.id
}

resource "aws_route_table" "egress" {

  vpc_id = aws_vpc.pilot.id
  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.egress.id
  }


}

resource "aws_route_table_association" "runtime" {
  subnet_id      = aws_subnet.runtime.id
  route_table_id = aws_route_table.egress.id
}

resource "aws_security_group" "runtime" {
  name_prefix = "dg-pilot-runtime-"
  vpc_id      = aws_vpc.pilot.id
}

resource "aws_vpc_security_group_egress_rule" "https" {

  security_group_id = aws_security_group.runtime.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 443
  to_port           = 443

}

resource "aws_vpc_security_group_egress_rule" "http_packages" {

  security_group_id = aws_security_group.runtime.id
  cidr_ipv4         = "0.0.0.0/0"
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80

}

resource "aws_vpc_security_group_ingress_rule" "bootstrap" {

  count             = var.bootstrap_cloudfront_ingress ? 1 : 0
  security_group_id = aws_security_group.runtime.id
  prefix_list_id    = data.aws_ec2_managed_prefix_list.cloudfront.id
  ip_protocol       = "tcp"
  from_port         = 80
  to_port           = 80

}


resource "aws_iam_role" "runtime" {

  name = "dg-pilot-runtime"
  assume_role_policy = jsonencode({
    Version = "2012-10-17", Statement = [{
      Effect = "Allow", Principal = {
        Service = "ec2.amazonaws.com"
      }, Action = "sts:AssumeRole"
      }
    ]
    }
  )

}

resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.runtime.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_instance_profile" "runtime" {
  name = "dg-pilot-runtime"
  role = aws_iam_role.runtime.name
}

resource "aws_iam_role_policy" "runtime" {

  role = aws_iam_role.runtime.id
  policy = jsonencode({
    Version = "2012-10-17", Statement = [
      {
        Effect = "Allow", Action = ["s3:GetObject"], Resource = ["${aws_s3_bucket.data.arn}/releases/*"]
      },
      {
        Effect = "Allow", Action = ["s3:PutObject"], Resource = ["${aws_s3_bucket.data.arn}/backups/*", "${aws_s3_bucket.data.arn}/deletions/*"]
      },
      {
        Effect = "Allow", Action = ["ssm:GetParameter"], Resource = ["arn:aws:ssm:${var.region}:${var.aws_account_id}:parameter/desktop-guide/pilot/database-password"]
      },
      {
        Effect = "Allow", Action = ["logs:CreateLogStream", "logs:PutLogEvents"], Resource = ["${aws_cloudwatch_log_group.api.arn}:*"]
      }

    ]
    }
  )

}

resource "aws_instance" "runtime" {

  ami                         = data.aws_ssm_parameter.ami.value
  instance_type               = var.instance_type
  subnet_id                   = aws_subnet.runtime.id
  vpc_security_group_ids      = [aws_security_group.runtime.id]
  iam_instance_profile        = aws_iam_instance_profile.runtime.name
  associate_public_ip_address = true
  credit_specification {
    cpu_credits = "standard"
  }

  metadata_options {
    http_tokens                 = "required"
    http_put_response_hop_limit = 2
    http_endpoint               = "enabled"
  }

  root_block_device {
    volume_size           = 30
    volume_type           = "gp3"
    encrypted             = true
    delete_on_termination = false
  }

  user_data                   = file("${path.module}/bootstrap.sh")
  user_data_replace_on_change = false
  tags = {
    Name = "dg-pilot-runtime"
  }

  lifecycle {
    prevent_destroy = true
  }


}

resource "aws_cloudfront_vpc_origin" "api" {

  vpc_origin_endpoint_config {

    name                   = "dg-pilot-api"
    arn                    = aws_instance.runtime.arn
    http_port              = 80
    https_port             = 443
    origin_protocol_policy = "http-only"
    origin_ssl_protocols {
      items    = ["TLSv1.2"]
      quantity = 1
    }


  }

  depends_on = [aws_internet_gateway.egress, aws_vpc_security_group_ingress_rule.bootstrap]

}

data "aws_security_group" "cloudfront_service" {

  filter {
    name   = "group-name"
    values = ["CloudFront-VPCOrigins-Service-SG"]
  }

  filter {
    name   = "vpc-id"
    values = [aws_vpc.pilot.id]
  }

  depends_on = [aws_cloudfront_vpc_origin.api]

}

resource "aws_vpc_security_group_ingress_rule" "private_origin" {

  security_group_id            = aws_security_group.runtime.id
  referenced_security_group_id = data.aws_security_group.cloudfront_service.id
  ip_protocol                  = "tcp"
  from_port                    = 80
  to_port                      = 80

}

resource "aws_cloudfront_distribution" "api" {

  enabled         = true
  comment         = "Desktop Guide isolated pilot API"
  is_ipv6_enabled = true
  aliases         = var.custom_domain == "" ? [] : [var.custom_domain]
  origin {

    domain_name = aws_instance.runtime.private_dns
    origin_id   = "api"
    vpc_origin_config {
      vpc_origin_id = aws_cloudfront_vpc_origin.api.id
      # Provider deadline is 35s including retries; never cut off a completed turn at 30s.
      origin_read_timeout = 60
    }


  }

  default_cache_behavior {

    target_origin_id         = "api"
    viewer_protocol_policy   = "https-only"
    allowed_methods          = ["GET", "HEAD", "OPTIONS", "PUT", "POST", "PATCH", "DELETE"]
    cached_methods           = ["GET", "HEAD"]
    cache_policy_id          = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad"
    origin_request_policy_id = "b689b0a8-53d0-40ab-baf2-68738e2966ac"
    compress                 = false

  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }

  }

  viewer_certificate {

    cloudfront_default_certificate = var.custom_domain == ""
    acm_certificate_arn            = var.custom_domain == "" ? null : var.acm_certificate_arn
    ssl_support_method             = var.custom_domain == "" ? null : "sni-only"
    # AWS fixes its default certificate's policy to TLSv1; custom SNI enables a stricter minimum.
    minimum_protocol_version = var.custom_domain == "" ? "TLSv1" : "TLSv1.2_2021"

  }

  dynamic "custom_error_response" {

    for_each = toset([400, 403, 404, 405, 414, 416, 500, 501, 502, 503, 504])
    content {
      error_code            = custom_error_response.value
      error_caching_min_ttl = 0
    }


  }


}


resource "aws_cognito_user_pool" "pilot" {

  name = "dg-pilot"
  admin_create_user_config {
    allow_admin_create_user_only = true
  }

  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]
  password_policy {
    minimum_length    = 12
    require_lowercase = true
    require_uppercase = true
    require_numbers   = true
    require_symbols   = true
  }

  deletion_protection = "ACTIVE"
  lifecycle {
    prevent_destroy = true
  }


}

resource "aws_cognito_resource_server" "api" {

  identifier   = "desktop-guide"
  name         = "Desktop Guide lessons"
  user_pool_id = aws_cognito_user_pool.pilot.id
  scope {
    scope_name        = "lessons"
    scope_description = "Use owner-scoped lessons"
  }


}

resource "aws_cognito_user_pool_client" "desktop" {

  name                                 = "dg-pilot-desktop"
  user_pool_id                         = aws_cognito_user_pool.pilot.id
  generate_secret                      = false
  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["openid", "desktop-guide/lessons"]
  supported_identity_providers         = ["COGNITO"]
  callback_urls                        = [var.callback_url]
  logout_urls                          = [var.callback_url]
  enable_token_revocation              = true
  prevent_user_existence_errors        = "ENABLED"
  depends_on                           = [aws_cognito_resource_server.api]

}

resource "aws_cognito_user_pool_domain" "pilot" {
  domain       = "dg-pilot-${var.aws_account_id}"
  user_pool_id = aws_cognito_user_pool.pilot.id
}

resource "aws_s3_bucket" "data" {

  bucket = "dg-pilot-data-${var.aws_account_id}"
  lifecycle {
    prevent_destroy = true
  }


}

resource "aws_s3_bucket_versioning" "data" {
  bucket = aws_s3_bucket.data.id
  versioning_configuration {
    status = "Enabled"
  }

}

resource "aws_s3_bucket_server_side_encryption_configuration" "data" {

  bucket = aws_s3_bucket.data.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }

  }


}

resource "aws_s3_bucket_public_access_block" "data" {

  bucket                  = aws_s3_bucket.data.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true

}

resource "aws_s3_bucket_policy" "data" {

  bucket = aws_s3_bucket.data.id
  policy = jsonencode({
    Version = "2012-10-17", Statement = [{
      Effect = "Deny", Principal = "*", Action = "s3:*", Resource = [aws_s3_bucket.data.arn, "${aws_s3_bucket.data.arn}/*"], Condition = {
        Bool = {
          "aws:SecureTransport" = "false"
        }

      }

      }
    ]
    }
  )

}

resource "aws_s3_bucket_lifecycle_configuration" "data" {

  bucket = aws_s3_bucket.data.id
  rule {
    id     = "deletion-ledger-retention"
    status = "Enabled"
    filter { prefix = "deletions/" }
    expiration { days = 30 }
    noncurrent_version_expiration { noncurrent_days = 1 }
  }
  rule {

    id     = "backup-retention"
    status = "Enabled"
    filter {
      prefix = "backups/"
    }

    expiration {
      days = 14
    }

    noncurrent_version_expiration {
      noncurrent_days = 14
    }

    abort_incomplete_multipart_upload {
      days_after_initiation = 1
    }


  }


}

resource "aws_cloudwatch_log_group" "api" {
  name              = "/desktop-guide/pilot/api"
  retention_in_days = 7
}

resource "aws_cloudwatch_metric_alarm" "instance" {

  alarm_name          = "dg-pilot-instance-check"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 2
  metric_name         = "StatusCheckFailed"
  namespace           = "AWS/EC2"
  period              = 60
  statistic           = "Maximum"
  threshold           = 0
  dimensions = {
    InstanceId = aws_instance.runtime.id
  }


}

resource "aws_cloudwatch_metric_alarm" "cpu" {

  alarm_name          = "dg-pilot-cpu"
  comparison_operator = "GreaterThanThreshold"
  evaluation_periods  = 3
  metric_name         = "CPUUtilization"
  namespace           = "AWS/EC2"
  period              = 300
  statistic           = "Average"
  threshold           = 80
  dimensions = {
    InstanceId = aws_instance.runtime.id
  }


}

resource "aws_budgets_budget" "pilot" {

  name         = "dg-pilot-monthly"
  budget_type  = "COST"
  limit_amount = "25"
  limit_unit   = "USD"
  time_unit    = "MONTHLY"
  dynamic "notification" {

    for_each = var.budget_alert_email == "" ? [] : [var.budget_alert_email]
    content {

      comparison_operator        = "GREATER_THAN"
      threshold                  = 80
      threshold_type             = "PERCENTAGE"
      notification_type          = "ACTUAL"
      subscriber_email_addresses = [notification.value]

    }


  }


}

output "api_origin" {
  value = "https://${aws_cloudfront_distribution.api.domain_name}"
}

output "runtime_instance_id" {
  value = aws_instance.runtime.id
}

output "runtime_role_arn" {
  value = aws_iam_role.runtime.arn
}

output "cognito_issuer" {
  value = "https://cognito-idp.${var.region}.amazonaws.com/${aws_cognito_user_pool.pilot.id}"
}

output "cognito_client_id" {
  value = aws_cognito_user_pool_client.desktop.id
}

output "cognito_domain" {
  value = "https://${aws_cognito_user_pool_domain.pilot.domain}.auth.${var.region}.amazoncognito.com"
}

output "data_bucket" {
  value = aws_s3_bucket.data.id
}
