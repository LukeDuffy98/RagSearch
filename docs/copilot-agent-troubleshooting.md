# Copilot Agent Troubleshooting Guide

## Quick Diagnostics

### Health Check Commands

```bash
# Check overall agent health
curl -H "Authorization: Bearer <function-key>" \
  https://your-function-app.azurewebsites.net/api/AgentHealthCheck

# Get detailed metrics
curl -H "Authorization: Bearer <function-key>" \
  https://your-function-app.azurewebsites.net/api/AgentMetrics

# Manual cache cleanup
curl -X POST -H "Authorization: Bearer <admin-key>" \
  https://your-function-app.azurewebsites.net/api/AgentCacheManagement
```

### Local Development

```bash
# Start development environment
./Scripts/start-dev.ps1

# Run diagnostics
./Scripts/debug-functions.ps1

# Test agent services locally
curl http://localhost:7071/api/AgentHealthCheck
```

## Common Issues

### 1. MCP Server Connection Issues

**Symptoms:**
- Health check shows MCP server as unhealthy
- "MCP server command not configured" error
- Circuit breaker open for MCP operations

**Troubleshooting Steps:**

1. **Check Configuration:**
   ```bash
   # Verify MCP configuration in local.settings.json
   grep -A 5 "MCP_SERVER" local.settings.json
   ```

2. **Test MCP Server Installation:**
   ```bash
   # Verify MCP GitHub server is available
   npx @modelcontextprotocol/server-github --help
   
   # Check Node.js version (requires v18+)
   node --version
   ```

3. **Check Network Connectivity:**
   ```bash
   # Test GitHub API access
   curl -H "Authorization: token $GITHUB_TOKEN" https://api.github.com/rate_limit
   ```

4. **Verify Environment Variables:**
   ```bash
   # Check if all required variables are set
   env | grep -E "(GITHUB_|MCP_)" | sort
   ```

**Common Solutions:**
- Install MCP server: `npm install -g @modelcontextprotocol/server-github`
- Update Node.js to version 18 or higher
- Verify GitHub token has correct scopes
- Check repository allowlist configuration

### 2. MS Docs Connectivity Issues

**Symptoms:**
- MS Docs connectivity test fails
- Cache miss rate extremely high
- "URL not in allowlist" errors

**Troubleshooting Steps:**

1. **Test Direct Connectivity:**
   ```bash
   # Test basic connectivity
   curl -I https://learn.microsoft.com
   curl -I https://docs.microsoft.com
   ```

2. **Check Allowlist Configuration:**
   ```bash
   # Verify allowed hosts
   echo $MSDOCS_ALLOWED_HOSTS
   ```

3. **Review Rate Limiting:**
   ```bash
   # Check rate limit status
   curl http://localhost:7071/api/AgentMetrics | jq '.rateLimitStatus.msdocs'
   ```

4. **Inspect Cache Status:**
   ```bash
   # Get cache statistics
   curl http://localhost:7071/api/AgentMetrics | jq '.cacheStatistics'
   ```

**Common Solutions:**
- Verify network access to Microsoft domains
- Check corporate firewall/proxy settings
- Review and update allowlist configuration
- Clear cache if corrupted: `POST /api/AgentCacheManagement`

### 3. Rate Limiting Issues

**Symptoms:**
- "Rate limit exceeded" errors
- High latency or timeouts
- Circuit breaker frequently opening

**Troubleshooting Steps:**

1. **Check Current Rate Limits:**
   ```bash
   # Get rate limit status for all services
   curl http://localhost:7071/api/AgentMetrics | jq '.rateLimitStatus'
   ```

2. **Review Configuration:**
   ```bash
   # Check rate limit settings
   env | grep "RATE_LIMIT"
   ```

3. **Monitor Usage Patterns:**
   ```bash
   # Check request metrics
   curl http://localhost:7071/api/AgentMetrics | jq '.metrics.counters'
   ```

**Common Solutions:**
- Increase rate limits if legitimate usage
- Implement request batching
- Add delays between requests
- Use circuit breaker status to back off

### 4. Authentication and Authorization Issues

**Symptoms:**
- "Unauthorized" errors from GitHub API
- Function key authentication failures
- Token expiration warnings

**Troubleshooting Steps:**

1. **Verify GitHub Token:**
   ```bash
   # Check token validity and scopes
   curl -H "Authorization: token $GITHUB_TOKEN" \
     https://api.github.com/user | jq '.login'
   
   # Check rate limit and expiration
   curl -H "Authorization: token $GITHUB_TOKEN" \
     https://api.github.com/rate_limit
   ```

2. **Test Function Keys:**
   ```bash
   # Use Azure CLI to get function keys
   az functionapp keys list --name your-function-app --resource-group your-rg
   ```

3. **Check Repository Access:**
   ```bash
   # Test access to allowed repository
   curl -H "Authorization: token $GITHUB_TOKEN" \
     https://api.github.com/repos/LukeDuffy98/RagSearch
   ```

**Common Solutions:**
- Regenerate GitHub token with correct scopes
- Update token in Key Vault or app settings
- Verify repository allowlist includes accessible repos
- Check function app authentication settings

### 5. Caching Issues

**Symptoms:**
- Low cache hit rate
- "Cache size limit reached" warnings
- Stale content being served

**Troubleshooting Steps:**

1. **Check Cache Statistics:**
   ```bash
   # Get detailed cache stats
   curl http://localhost:7071/api/AgentMetrics | jq '.cacheStatistics'
   ```

2. **Review Cache Configuration:**
   ```bash
   # Check TTL and size settings
   env | grep -E "(CACHE_|TTL)"
   ```

3. **Test Cache Operations:**
   ```bash
   # Manual cache cleanup
   curl -X POST http://localhost:7071/api/AgentCacheManagement
   
   # Invalidate specific URL
   curl -X DELETE -H "Content-Type: application/json" \
     -d '{"url":"https://learn.microsoft.com/some-page"}' \
     http://localhost:7071/api/AgentCacheManagement
   ```

**Common Solutions:**
- Increase cache size limit if memory allows
- Adjust TTL based on content freshness needs
- Implement cache warming for frequently accessed content
- Monitor and tune cache efficiency metrics

### 6. Logging and Observability Issues

**Symptoms:**
- Missing or incomplete logs
- Secrets appearing in logs
- Correlation ID tracking issues

**Troubleshooting Steps:**

1. **Check Log Configuration:**
   ```bash
   # Verify logging settings
   env | grep -E "(LOG_|AGENT_LOG)"
   ```

2. **Test Log Redaction:**
   ```bash
   # Check if sensitive data is being redacted
   grep -i "bearer\|token\|password" /path/to/logs
   ```

3. **Verify Application Insights:**
   ```bash
   # Check Application Insights connection
   az monitor app-insights component show --app your-app-insights
   ```

**Common Solutions:**
- Enable verbose logging for debugging: `ENABLE_VERBOSE_LOGGING=true`
- Verify log redaction patterns are working
- Check Application Insights connection string
- Review log level configuration

## Performance Optimization

### Memory Usage

**Monitor Memory:**
```bash
# Check function app memory usage
az functionapp show --name your-function-app --resource-group your-rg \
  --query "siteConfig.alwaysOn"
```

**Optimize Cache:**
- Reduce cache size if memory pressure detected
- Implement cache partitioning for large datasets
- Use compression for cached content

### Network Latency

**Measure Latency:**
```bash
# Test endpoint response times
curl -w "%{time_total}\n" -o /dev/null -s http://localhost:7071/api/AgentHealthCheck
```

**Optimization Strategies:**
- Enable HTTP/2 for external requests
- Implement connection pooling
- Use CDN for static MS Docs content

### Resource Utilization

**Monitor Resources:**
```bash
# Check function execution metrics
curl http://localhost:7071/api/AgentMetrics | jq '.derivedMetrics'
```

**Optimization:**
- Adjust function timeout settings
- Implement async processing for long operations
- Use queues for background processing

## Maintenance Tasks

### Daily

- [ ] Check health check endpoint status
- [ ] Review rate limit usage
- [ ] Monitor cache hit rates
- [ ] Check for any circuit breaker openings

### Weekly

- [ ] Review Application Insights dashboards
- [ ] Clean up expired cache entries
- [ ] Check GitHub token expiration
- [ ] Update dependency vulnerabilities

### Monthly

- [ ] Review and rotate GitHub tokens
- [ ] Update MCP server to latest version
- [ ] Analyze usage patterns and optimize
- [ ] Security review of logs and access

## Emergency Procedures

### Service Outage

1. **Immediate Assessment:**
   - Check health check endpoint
   - Verify external service availability
   - Review recent deployments

2. **Escalation:**
   - Contact on-call engineer
   - Create incident ticket
   - Notify stakeholders

3. **Recovery:**
   - Restart function app if needed
   - Clear caches if corrupted
   - Verify service restoration

### Security Incident

1. **Isolation:**
   - Disable affected endpoints
   - Revoke compromised tokens
   - Preserve logs for investigation

2. **Investigation:**
   - Review access logs
   - Check for data exposure
   - Identify attack vectors

3. **Recovery:**
   - Apply security patches
   - Update authentication
   - Restore services gradually

## Support Contacts

- **On-Call Engineer:** +1-XXX-XXX-XXXX
- **Security Team:** security@example.com
- **DevOps Team:** devops@example.com

## Useful Links

- [Azure Function App Logs](https://portal.azure.com)
- [Application Insights Dashboard](https://portal.azure.com)
- [GitHub Repository](https://github.com/LukeDuffy98/RagSearch)
- [MCP Documentation](https://modelcontextprotocol.io)