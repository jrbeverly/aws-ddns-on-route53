provider "aws" {
  region = var.region
}

data "aws_region" "current" {}

resource "aws_dynamodb_table" "records" {
  name         = "DynamicDnsRecords"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "Hostname"

  attribute {
    name = "Hostname"
    type = "S"
  }
}

data "aws_iam_policy_document" "lambda_assume" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["lambda.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "lambda_exec" {
  name               = "dynamic-dns-lambda-role"
  assume_role_policy = data.aws_iam_policy_document.lambda_assume.json
}

resource "aws_iam_policy" "lambda_dynamo" {
  name = "dynamic-dns-dynamodb-access"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "dynamodb:DescribeTable",
        "dynamodb:GetItem",
        "dynamodb:PutItem"
      ]
      Resource = aws_dynamodb_table.records.arn
    }]
  })
}

resource "aws_iam_policy" "lambda_route53" {
  name = "dynamic-dns-route53-access"
  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect = "Allow"
      Action = [
        "route53:ListResourceRecordSets",
        "route53:ChangeResourceRecordSets"
      ]
      Resource = "arn:aws:route53:::hostedzone/${var.hosted_zone_id}"
    }]
  })
}

resource "aws_cloudwatch_log_group" "dynamic_dns" {
  name              = "/aws/lambda/${aws_lambda_function.dynamic_dns.function_name}"
  retention_in_days = 14
}

resource "aws_iam_role_policy_attachment" "dynamo_attach" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = aws_iam_policy.lambda_dynamo.arn
}

resource "aws_iam_role_policy_attachment" "route53_attach" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = aws_iam_policy.lambda_route53.arn
}

resource "aws_iam_role_policy_attachment" "logs_attach" {
  role       = aws_iam_role.lambda_exec.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

resource "aws_lambda_function" "dynamic_dns" {
  function_name    = "dynamic-dns-service"
  handler          = "DynamicDnsService::DynamicDnsService.Function::FunctionHandler"
  runtime          = "dotnet8"
  role             = aws_iam_role.lambda_exec.arn
  filename         = var.lambda_package
  source_code_hash = filebase64sha256(var.lambda_package)
  memory_size      = 128
  timeout          = 10

  environment {
    variables = {
      DDB_TABLE      = aws_dynamodb_table.records.name
      HOSTED_ZONE_ID = var.hosted_zone_id
    }
  }
}

variable "hosted_zone_id" {
  description = "ID of the Route53 hosted zone the Lambda is permitted to manage"
}

variable "region" {
  default = "us-east-1"
}

variable "lambda_package" {
  description = "Path to your Lambda ZIP"
  default     = "lambda.zip"
}

variable "stage_name" {
  default = "prod"
}

resource "aws_api_gateway_rest_api" "api" {
  name = "dynamic-dns-api"
}

# catch‑all under the root
resource "aws_api_gateway_resource" "proxy" {
  rest_api_id = aws_api_gateway_rest_api.api.id
  parent_id   = aws_api_gateway_rest_api.api.root_resource_id
  path_part   = "{proxy+}"
}

# ANY method on the proxy
resource "aws_api_gateway_method" "all_methods" {
  rest_api_id   = aws_api_gateway_rest_api.api.id
  resource_id   = aws_api_gateway_resource.proxy.id
  http_method   = "ANY"
  authorization = "NONE"
  request_parameters = {
    "method.request.path.proxy" = true
  }
}

resource "aws_api_gateway_integration" "all_integrations" {
  rest_api_id             = aws_api_gateway_rest_api.api.id
  resource_id             = aws_api_gateway_resource.proxy.id
  http_method             = aws_api_gateway_method.all_methods.http_method
  type                    = "AWS_PROXY"
  integration_http_method = "POST"
  uri                     = aws_lambda_function.dynamic_dns.invoke_arn
  request_parameters = {
    "integration.request.path.proxy" = "method.request.path.proxy"
  }
}

resource "aws_api_gateway_deployment" "deployment" {
  rest_api_id = aws_api_gateway_rest_api.api.id
  stage_name  = var.stage_name
  depends_on  = [aws_api_gateway_integration.all_integrations]
}

resource "aws_lambda_permission" "apigw" {
  statement_id  = "AllowAPIGatewayInvoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.dynamic_dns.function_name
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_api_gateway_rest_api.api.execution_arn}/*/*"
}

output "api_endpoint" {
  value = "https://${aws_api_gateway_rest_api.api.id}.execute-api.${data.aws_region.current.name}.amazonaws.com/${var.stage_name}"
}