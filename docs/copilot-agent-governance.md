# Copilot Agent Governance and Compliance

## Overview

This document outlines the governance, license compliance, and data retention policies for the Copilot Agent Coder environment in RagSearch.

## License and Terms of Service Compliance

### Microsoft Docs Usage

**Permitted Use:**
- Educational and research purposes within the scope of software development
- Caching content for up to 24 hours to reduce API load
- Attribution and source citation in all responses

**Restrictions:**
- No redistribution of cached Microsoft Docs content
- No commercial resale of processed Microsoft Docs information
- Must respect Microsoft's robots.txt and rate limits
- Must provide proper attribution: "Source: Microsoft Docs (docs.microsoft.com)"

**Implementation:**
- Cache TTL limited to 24 hours (configurable via `MSDOCS_CACHE_TTL_SECONDS`)
- Rate limiting: max 1000 requests per hour (configurable via `MSDOCS_RATE_LIMIT_PER_HOUR`)
- Allowlist-only access to `learn.microsoft.com` and `docs.microsoft.com`
- Source citations required in all responses using external documentation

### GitHub API Usage

**Permitted Use:**
- Read operations on allowed repositories only
- Limited write operations (issues, PRs) with least-privilege tokens
- Rate limiting respects GitHub's API limits

**Restrictions:**
- Repository access limited to allowlist (`GITHUB_REPO_ALLOWLIST`)
- No access to private repositories without explicit permission
- No bulk data extraction or scraping
- Token rotation required every 90 days

**Implementation:**
- Least-privilege GitHub tokens with minimal required scopes:
  - `repo` (only for allowed repositories)
  - `read:user` (for user context)
  - `read:org` (if organization access needed)
- Rate limiting: max 5000 requests per hour (GitHub's standard limit)
- Circuit breaker pattern to prevent abuse
- Token expiration monitoring in drift detection

## Data Retention Policy

### Cached Content

**Microsoft Docs Content:**
- **Retention Period:** 24 hours (default, configurable)
- **Storage Location:** In-memory cache only
- **Purge Policy:** Automatic expiration based on TTL
- **Manual Invalidation:** Available via cache management API
- **Size Limits:** Maximum 10,000 cached items, automatic cleanup on overflow

**GitHub Data:**
- **Retention Period:** Session-based, no persistent storage
- **Storage Location:** Memory only during request processing
- **Purge Policy:** Immediate cleanup after request completion
- **Exception:** Metrics and logs may contain non-sensitive metadata

### Logs and Telemetry

**Application Logs:**
- **Retention Period:** 30 days in Azure Application Insights
- **Content:** Redacted logs with no secrets or PII
- **Storage Location:** Azure Application Insights
- **Access Control:** Restricted to authorized personnel only

**Metrics Data:**
- **Retention Period:** 90 days for operational metrics
- **Content:** Aggregated counters, latencies, error rates
- **Storage Location:** In-memory for real-time, Application Insights for historical
- **Purge Policy:** Automatic rollover after retention period

**Security Events:**
- **Retention Period:** 1 year for security and audit logs
- **Content:** Authentication failures, rate limit violations, circuit breaker events
- **Storage Location:** Azure Application Insights with extended retention
- **Access Control:** SOC team and authorized security personnel only

### Personal Identifiable Information (PII)

**Data Minimization:**
- No PII stored in cache or logs
- Correlation IDs used instead of user identifiers
- GitHub usernames only in request context, not persisted

**Redaction Policy:**
- Automatic redaction of tokens, API keys, passwords in logs
- Regular expression patterns for sensitive data detection
- Manual review of log patterns quarterly

## Compliance Monitoring

### Automated Checks

**Daily Drift Detection:**
- GitHub token validity and expiration warnings
- Rate limit usage monitoring
- MS Docs endpoint availability
- Dependency security updates

**Cache Compliance:**
- TTL enforcement monitoring
- Size limit compliance
- Automatic cleanup verification
- Manual invalidation audit trail

### Manual Reviews

**Quarterly Security Review:**
- Access control verification
- Log redaction effectiveness
- Token scope validation
- Data retention compliance

**Annual Policy Review:**
- License compliance assessment
- Data retention policy updates
- Security control effectiveness
- Third-party dependency audit

## Incident Response

### Data Breach Response

**Immediate Actions (0-24 hours):**
1. Isolate affected systems
2. Assess scope of potential data exposure
3. Notify security team and stakeholders
4. Preserve evidence and logs

**Short-term Actions (1-7 days):**
1. Complete forensic analysis
2. Implement containment measures
3. Notify affected users if applicable
4. Update security controls

**Long-term Actions (7-30 days):**
1. Root cause analysis
2. Policy and procedure updates
3. Training and awareness updates
4. Compliance reporting

### Token Compromise Response

**Immediate Actions:**
1. Revoke compromised tokens
2. Generate new tokens with minimal scope
3. Review access logs for suspicious activity
4. Update token in secure configuration

**Follow-up Actions:**
1. Investigate root cause of compromise
2. Enhance token security measures
3. Update incident response procedures
4. Schedule security awareness training

## Access Control

### Administrative Access

**System Administrators:**
- Full access to configuration and logs
- Quarterly access review required
- Multi-factor authentication mandatory
- Activity logging and monitoring

**Developers:**
- Read-only access to non-sensitive logs
- No access to production tokens or secrets
- Code review required for security changes
- Security training required annually

**Security Team:**
- Full access for security monitoring
- Incident response privileges
- Audit trail access
- Compliance reporting capabilities

### API Access

**Health Check Endpoint:**
- Authorization Level: Function key required
- Rate Limited: 100 requests per hour per client
- Monitoring: All access logged with correlation ID

**Metrics Endpoint:**
- Authorization Level: Function key required
- Rate Limited: 50 requests per hour per client
- Monitoring: Access patterns monitored for abuse

**Cache Management:**
- Authorization Level: Admin key required
- Rate Limited: 10 requests per hour per client
- Monitoring: All operations audited and logged

## Third-Party Dependencies

### MCP Server Dependencies

**@modelcontextprotocol/server-github:**
- Version pinning required
- Security vulnerability monitoring
- Quarterly update assessment
- License compliance verification

### .NET Dependencies

**Azure SDKs:**
- Latest stable versions preferred
- Security updates applied within 30 days
- Vulnerability scanning in CI/CD pipeline
- License compatibility verified

## Contact Information

**Security Team:** security@example.com
**Compliance Officer:** compliance@example.com
**Data Protection Officer:** dpo@example.com

## Document Version

- **Version:** 1.0
- **Last Updated:** $(date)
- **Next Review:** $(date --date="+3 months")
- **Approved By:** Security Team and Legal