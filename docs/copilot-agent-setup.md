# Copilot Agent Coder Environment Setup

## Overview

This document provides a complete setup guide for the Copilot Agent Coder environment in RagSearch, including MCP (Model Context Protocol) servers for GitHub integration and Microsoft Docs connectivity.

## Architecture

```mermaid
graph TB
    subgraph "Copilot Agent Environment"
        A[Copilot Agent] --> B[RagSearch Functions]
        B --> C[MCP GitHub Server]
        B --> D[MS Docs Cache Service]
        B --> E[Resilience Services]
    end
    
    subgraph "External Services"
        F[GitHub API]
        G[Microsoft Learn]
        H[Microsoft Docs]
    end
    
    subgraph "Observability"
        I[Health Checks]
        J[Metrics Collection]
        K[Secure Logging]
        L[Application Insights]
    end
    
    C --> F
    D --> G
    D --> H
    B --> I
    B --> J
    B --> K
    K --> L
```

## Quick Start

### 1. Prerequisites

**Required Tools:**
- .NET 8.0 SDK
- Azure Functions Core Tools v4
- Node.js 18+ (for MCP server)
- PowerShell 7+ (for testing scripts)

**Optional Tools:**
- Azure CLI (for deployment)
- Visual Studio 2022 or VS Code

### 2. Environment Setup

**Install MCP GitHub Server:**
```bash
npm install -g @modelcontextprotocol/server-github
```

**Verify Installation:**
```bash
npx @modelcontextprotocol/server-github --version
```

**Configure local.settings.json:**
```bash
cp local.settings.template.json local.settings.json
# Edit local.settings.json with your actual values
```

### 3. Configuration

**Required Configuration Fields:**

```json
{
  "IsEncrypted": false,
  "Values": {
    // Basic Azure Functions settings
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    // GitHub MCP Configuration
    "GITHUB_TOKEN": "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx",
    "GITHUB_REPO_ALLOWLIST": "LukeDuffy98/RagSearch",
    "GITHUB_API_BASE": "https://api.github.com",
    
    // MS Docs Configuration
    "MSDOCS_ALLOWED_HOSTS": "learn.microsoft.com,docs.microsoft.com",
    "MSDOCS_CACHE_TTL_SECONDS": "86400",
    
    // MCP Server Settings
    "MCP_SERVER_TRANSPORT": "stdio",
    "MCP_SERVER_COMMAND": "npx @modelcontextprotocol/server-github",
    "MCP_SERVER_HEALTH_CHECK_INTERVAL_MS": "60000",
    "MCP_SERVER_MAX_RETRIES": "3",
    "MCP_SERVER_RETRY_DELAY_MS": "1000",
    
    // Rate Limiting
    "GITHUB_RATE_LIMIT_PER_HOUR": "5000",
    "MSDOCS_RATE_LIMIT_PER_HOUR": "1000",
    "CIRCUIT_BREAKER_FAILURE_THRESHOLD": "5",
    "CIRCUIT_BREAKER_TIMEOUT_MS": "60000",
    
    // Observability
    "AGENT_LOG_LEVEL": "info",
    "AGENT_REQUEST_TIMEOUT_MS": "30000",
    "ENABLE_VERBOSE_LOGGING": "false",
    "ENABLE_METRICS_COLLECTION": "true",
    "LOG_REDACTION_ENABLED": "true",
    "TELEMETRY_CORRELATION_ID_HEADER": "x-correlation-id"
  }
}
```

### 4. GitHub Token Setup

**Required Scopes:**
- `repo` (for repository access)
- `read:user` (for user context)
- `read:org` (if organization access needed)

**Security Best Practices:**
- Use fine-grained personal access tokens when available
- Limit repository access to specific repositories only
- Set token expiration to 90 days maximum
- Rotate tokens regularly using the drift detection schedule

**Token Creation:**
1. Go to GitHub Settings → Developer settings → Personal access tokens
2. Generate new token (fine-grained if available)
3. Select minimal required permissions
4. Set expiration date
5. Copy token to `local.settings.json`

### 5. Development Workflow

**Start Development Environment:**
```bash
# Start all services
./Scripts/start-dev.ps1

# Or manually:
# 1. Start Azurite
azurite --silent --location ./azurite &

# 2. Build and start functions
dotnet build
func start
```

**Run Comprehensive Tests:**
```bash
# Test all Copilot Agent functionality
./Scripts/test-copilot-agent.ps1 -TestType All -DetailedOutput

# Test specific components
./Scripts/test-copilot-agent.ps1 -TestType Health
./Scripts/test-copilot-agent.ps1 -TestType MCP
./Scripts/test-copilot-agent.ps1 -TestType MSdocs
```

## Key Features

### 1. MCP Server Integration

**Health Monitoring:**
- Automatic configuration validation
- Connection testing and retry logic
- Circuit breaker protection for unstable connections

**GitHub Operations:**
- Rate-limited API access with exponential backoff
- Repository allowlist enforcement
- Least-privilege token scoping

### 2. Microsoft Docs Caching

**Smart Caching:**
- 24-hour TTL (configurable)
- LRU eviction policy
- Automatic cleanup and compression

**Content Validation:**
- Host allowlist enforcement
- Content-Type validation
- ETag and Last-Modified support

### 3. Resilience Patterns

**Rate Limiting:**
- Per-service rate limiting (GitHub, MS Docs)
- Sliding window implementation
- Configurable limits and monitoring

**Circuit Breaker:**
- Automatic failure detection
- Configurable thresholds and timeouts
- Half-open state for recovery testing

**Retries:**
- Exponential backoff with jitter
- Configurable max attempts
- Operation-specific timeout handling

### 4. Observability

**Structured Logging:**
- Automatic secret redaction
- Correlation ID tracking
- Configurable log levels

**Metrics Collection:**
- Request counts and latencies
- Error rates and circuit breaker states
- Cache hit rates and efficiency

**Health Checks:**
- Deep health status reporting
- Dependency health aggregation
- Performance metrics inclusion

## API Endpoints

### Health Check
```http
GET /api/AgentHealthCheck
Authorization: Bearer <function-key>
```

**Response:**
```json
{
  "correlationId": "abc-123",
  "timestamp": "2024-01-15T10:30:00Z",
  "overallStatus": "Healthy",
  "responseTimeMs": 245.7,
  "mcpServerHealth": {
    "isHealthy": true,
    "status": "Healthy",
    "responseTime": "00:00:00.1234567"
  },
  "msDocsConnectivity": {
    "isConnected": true,
    "testedUrls": ["https://learn.microsoft.com", "https://docs.microsoft.com"]
  },
  "configuration": {
    "mcpCommand": true,
    "allowedReposConfigured": true,
    "allowedHostsConfigured": true,
    "cacheTtlSeconds": 86400
  }
}
```

### Metrics
```http
GET /api/AgentMetrics
Authorization: Bearer <function-key>
```

### Cache Management
```http
# Cleanup cache
POST /api/AgentCacheManagement
Authorization: Bearer <admin-key>

# Invalidate specific URL
DELETE /api/AgentCacheManagement
Authorization: Bearer <admin-key>
Content-Type: application/json

{
  "url": "https://learn.microsoft.com/page-to-invalidate"
}
```

## Deployment

### Azure Function App Configuration

**App Settings:**
```bash
# Use Azure CLI to set app settings
az functionapp config appsettings set \
  --name myapp --resource-group myrg \
  --settings @production-settings.json
```

**Managed Identity:**
```bash
# Enable system-assigned managed identity
az functionapp identity assign \
  --name myapp --resource-group myrg
```

### CI/CD Pipeline

The included GitHub Actions workflow (`.github/workflows/copilot-agent-ci.yml`) provides:

- **Build and Test:** Automated building and unit testing
- **Smoke Tests:** Integration testing of all agent services
- **Drift Detection:** Daily monitoring of token expiration and service availability
- **Deployment:** Automated deployment to Azure with health checks

**Required Secrets:**
- `GITHUB_TOKEN`: GitHub token for testing and drift detection
- `AZURE_CREDENTIALS`: Azure service principal for deployment

### Production Considerations

**Security:**
- Store secrets in Azure Key Vault
- Use managed identity for Azure services
- Enable Application Insights for monitoring
- Configure network security groups

**Performance:**
- Use Premium plan for better cold start performance
- Configure appropriate timeout values
- Monitor memory usage and scale accordingly
- Enable Application Insights for performance tracking

**Reliability:**
- Set up alerts for health check failures
- Configure auto-scaling based on load
- Use deployment slots for zero-downtime deployments
- Monitor rate limit usage and adjust as needed

## Troubleshooting

### Common Issues

1. **MCP Server Not Found**
   - Install: `npm install -g @modelcontextprotocol/server-github`
   - Verify Node.js version: `node --version` (requires 18+)

2. **GitHub API Rate Limits**
   - Check rate limit status: `GET /api/AgentMetrics`
   - Verify token scopes and expiration
   - Consider increasing rate limit thresholds

3. **MS Docs Connectivity**
   - Verify allowlist configuration
   - Check corporate firewall settings
   - Test direct connectivity to endpoints

4. **Cache Issues**
   - Check cache statistics in metrics
   - Manual cleanup: `POST /api/AgentCacheManagement`
   - Verify TTL configuration

### Diagnostic Commands

```bash
# Health check
curl http://localhost:7071/api/AgentHealthCheck

# Metrics
curl http://localhost:7071/api/AgentMetrics

# Comprehensive test suite
./Scripts/test-copilot-agent.ps1 -TestType All -DetailedOutput

# Debug specific issues
./Scripts/debug-functions.ps1
```

## Documentation Links

- [Governance and Compliance](docs/copilot-agent-governance.md)
- [Troubleshooting Guide](docs/copilot-agent-troubleshooting.md)
- [Compatibility Matrix](docs/copilot-agent-compatibility.md)
- [Copilot Instructions](docs/copilot-instructions.md)

## Support

- **Technical Issues:** GitHub Issues in this repository
- **Security Concerns:** security@example.com
- **Emergency Support:** See [troubleshooting guide](docs/copilot-agent-troubleshooting.md)

---

*For the most up-to-date information, always refer to the documentation in the `docs/` folder and the latest version of this setup guide.*