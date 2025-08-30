# Azure Functions Deployment Script

param(
    [Parameter(Mandatory=$true)]
    [string]$ResourceGroupName,
    
    [Parameter(Mandatory=$false)]
    [string]$Location = "East US",
    
    [Parameter(Mandatory=$false)]
    [string]$FunctionAppName = "ragsearch-$(Get-Random -Minimum 1000 -Maximum 9999)"
)

Write-Host "Azure Functions Deployment Script" -ForegroundColor Green
Write-Host "==================================" -ForegroundColor Green
Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor Cyan
Write-Host "Location: $Location" -ForegroundColor Cyan
Write-Host "Function App Name: $FunctionAppName" -ForegroundColor Cyan
Write-Host ""

# Check if Azure CLI is installed
$azVersion = az version --output tsv 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Azure CLI detected" -ForegroundColor Green
} else {
    Write-Host "❌ Azure CLI not found. Please install from: https://aka.ms/azurecli" -ForegroundColor Red
    exit 1
}

# Check if logged in to Azure
$account = az account show --output tsv 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Not logged in to Azure. Please run 'az login'" -ForegroundColor Red
    exit 1
} else {
    Write-Host "✅ Logged in to Azure" -ForegroundColor Green
}

try {
    # Build the project
    Write-Host "Building the project..." -ForegroundColor Yellow
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Host "✅ Build successful" -ForegroundColor Green

    # Create resource group if it doesn't exist
    Write-Host "Creating resource group '$ResourceGroupName'..." -ForegroundColor Yellow
    az group create --name $ResourceGroupName --location $Location --output none
    Write-Host "✅ Resource group ready" -ForegroundColor Green

    # Deploy infrastructure using Bicep
    Write-Host "Deploying infrastructure..." -ForegroundColor Yellow
    $deploymentResult = az deployment group create `
        --resource-group $ResourceGroupName `
        --template-file infra/main.bicep `
        --parameters infra/main.parameters.json `
        --parameters appName=$FunctionAppName `
        --output json | ConvertFrom-Json
    
    if ($LASTEXITCODE -ne 0) {
        throw "Infrastructure deployment failed"
    }
    
    $actualFunctionAppName = $deploymentResult.properties.outputs.functionAppName.value
    Write-Host "✅ Infrastructure deployed successfully" -ForegroundColor Green
    Write-Host "Function App Name: $actualFunctionAppName" -ForegroundColor Cyan

    # Publish the function app
    Write-Host "Publishing function app..." -ForegroundColor Yellow
    func azure functionapp publish $actualFunctionAppName
    if ($LASTEXITCODE -ne 0) {
        throw "Function app publish failed"
    }
    Write-Host "✅ Function app published successfully" -ForegroundColor Green

    # Get function app URL
    $functionAppUrl = "https://$actualFunctionAppName.azurewebsites.net"
    
    Write-Host ""
    Write-Host "Deployment Complete!" -ForegroundColor Green
    Write-Host "==================" -ForegroundColor Green
    Write-Host "Function App URL: $functionAppUrl" -ForegroundColor Cyan
    Write-Host "Resource Group: $ResourceGroupName" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Test your function:" -ForegroundColor Yellow
    Write-Host "curl $functionAppUrl/api/HttpExample?name=Azure" -ForegroundColor Gray
    Write-Host ""
    
} catch {
    Write-Host "❌ Deployment failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}