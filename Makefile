LAMBDA_PROJ  := DynamicDnsService/src/DynamicDnsService/DynamicDnsService.csproj
LAMBDA_TESTS := DynamicDnsService/test/DynamicDnsService.Tests/DynamicDnsService.Tests.csproj
CLIENT_TESTS := DdnsClient/test/DdnsClient.Tests/DdnsClient.Tests.csproj

HOSTED_ZONE_ID ?= Z0156236NAB5PLT3EVOT
RECORD_NAME    ?=
export HOSTED_ZONE_ID RECORD_NAME

TF_VARS := -var hosted_zone_id=$(HOSTED_ZONE_ID)

export DOTNET_NOLOGO := 1
export DOTNET_CLI_TELEMETRY_OPTOUT := 1

.PHONY: build test package deploy e2e destroy

build:
	dotnet build $(LAMBDA_TESTS)
	dotnet build $(CLIENT_TESTS)

test: build
	dotnet test $(LAMBDA_TESTS) --no-build
	dotnet test $(CLIENT_TESTS) --no-build

package:
	rm -rf .build infra/lambda.zip
	dotnet publish $(LAMBDA_PROJ) -c Release -o .build/lambda
	cd .build/lambda && zip -qr ../../infra/lambda.zip .

deploy: package
	terraform -chdir=infra init -input=false
	terraform -chdir=infra apply -auto-approve -input=false $(TF_VARS)

e2e: build
	./e2e.sh

destroy:
	terraform -chdir=infra destroy -auto-approve -input=false $(TF_VARS)
