# GitHub Copilot Chat Instructions for RagSearch Azure Functions

## 🎯 Project Context

You are working on **RagSearch**, a C# Azure Functions project built with .NET 8.0 and the isolated process model. The project emphasizes **simplicity**, **maintainability**, and **comprehensive testing**. This project now includes **Copilot Agent Coder environment** with MCP (Model Context Protocol) servers and Microsoft Docs integration.

## 📋 Core Principles

### 1. **Simplicity First**
- Keep functions focused on single responsibilities
- Avoid complex inheritance hierarchies
- Use dependency injection sparingly and purposefully
- Prefer composition over complex abstractions
- Write self-documenting code with clear naming

### 2. **Maintainability**
- Each function should be easily testable in isolation
- Use consistent patterns across all functions
- Implement comprehensive logging for debugging
- Follow established naming conventions
- Keep configuration externalized

### 3. **Testing Philosophy**
- Every function must have corresponding unit tests
- Integration tests should cover end-to-end scenarios
- PowerShell scripts should automate all testing workflows
- Debug logs should provide clear troubleshooting information

### 4. **RAG and Source Citation Requirements** ⭐ **NEW**
- **Always use RagSearch's index/vector store** for grounding responses in repository documentation
- **Require source citations** when using external documentation (Microsoft Docs, GitHub)
- **Preserve context** from the repository's existing knowledge base
- **Validate information** against the repository's documentation before providing answers
- **Format citations** as: `Source: [Title](URL)` or `According to [Source]: ...`

### 5. **Copilot Agent Security and Governance** ⭐ **NEW**
- **Least-privilege principle** for all external API access
- **Rate limiting and circuit breakers** for external service calls
- **Secret redaction** in all logging and responses
- **Allowlist-only access** to external resources (GitHub repos, MS Docs hosts)
- **Audit trail** for all agent operations and external API calls

## 🏗️ Project Structure Guidelines

```
RagSearch/
├── Functions/              # All Azure Functions
├── Models/                 # Data models and DTOs
├── Services/               # Business logic services
├── Tests/                  # All test projects
├── Scripts/                # PowerShell testing scripts
└── docs/                   # Documentation
```

## 🔧 Development Guidelines

### Function Implementation
When creating new Azure Functions:

1. **Use the existing pattern**:
   ```csharp
   public class MyFunction
   {
       private readonly ILogger _logger;
       
       public MyFunction(ILoggerFactory loggerFactory)
       {
           _logger = loggerFactory.CreateLogger<MyFunction>();
       }
       
       [Function("FunctionName")]
       public async Task<HttpResponseData> Run([HttpTrigger] HttpRequestData req)
       {
           _logger.LogInformation("Function started");
           // Implementation
       }
   }
   ```

2. **Always include error handling**:
   ```csharp
   try
   {
       // Function logic
       _logger.LogInformation("Operation completed successfully");
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, "Function failed: {ErrorMessage}", ex.Message);
       throw;
   }
   ```

3. **Use structured logging**:
   ```csharp
   _logger.LogInformation("Processing request for user {UserId} with operation {Operation}", 
                         userId, operationName);
   ```

### Service Implementation
When creating services:

1. **Define interfaces first**:
   ```csharp
   public interface IDataService
   {
       Task<Data> GetDataAsync(string id);
       Task SaveDataAsync(Data data);
   }
   ```

2. **Keep implementations simple**:
   ```csharp
   public class DataService : IDataService
   {
       private readonly ILogger<DataService> _logger;
       
       // Simple, focused implementation
   }
   ```

3. **Register in Program.cs**:
   ```csharp
   services.AddScoped<IDataService, DataService>();
   ```

## 🧪 Testing Requirements

### Unit Tests
Every function and service must have unit tests:

```csharp
[TestClass]
public class HttpTriggerFunctionTests
{
    private readonly Mock<ILogger<HttpTriggerFunction>> _mockLogger;
    private readonly HttpTriggerFunction _function;
    
    public HttpTriggerFunctionTests()
    {
        _mockLogger = new Mock<ILogger<HttpTriggerFunction>>();
        _function = new HttpTriggerFunction(_mockLogger.Object);
    }
    
    [TestMethod]
    public async Task Run_ValidRequest_ReturnsSuccess()
    {
        // Arrange, Act, Assert
    }
}
```

### Integration Tests
Create integration tests for complete workflows:

```csharp
[TestClass]
public class FunctionIntegrationTests
{
    [TestMethod]
    public async Task HttpFunction_EndToEnd_Success()
    {
        // Test complete HTTP request/response cycle
    }
}
```

### PowerShell Test Scripts
Use the comprehensive PowerShell tooling in the `Scripts/` folder:
- `start-dev.ps1` - Complete development environment setup and management
- `debug-functions.ps1` - Quick debugging and system diagnostics
- `test-suite.ps1` - Comprehensive automated testing with HTML reports
- `run-unit-tests.ps1` - Execute all unit tests (legacy)
- `run-integration-tests.ps1` - Execute integration tests (legacy)
- `test-functions-locally.ps1` - Test running functions (legacy)
- `debug-function.ps1` - Debug specific function with detailed logging (legacy)

## 🐛 Debugging and Logging

### Logging Levels
Use appropriate log levels:
- **LogTrace**: Detailed debugging (development only)
- **LogDebug**: Debug information for troubleshooting
- **LogInformation**: General operational information
- **LogWarning**: Potential issues that don't stop execution
- **LogError**: Errors that stop current operation
- **LogCritical**: System failures

### Debug Information
Always include relevant context in logs:
```csharp
_logger.LogInformation("Function {FunctionName} processing {RequestType} request at {Timestamp}",
                      nameof(HttpExample), req.Method, DateTime.UtcNow);
```

## 🚀 Deployment Guidelines

### Local Development
- Always use Azurite for local storage
- Test all functions locally before deployment
- Verify all dependencies are properly configured
- Run complete test suite before committing

### Azure Deployment
- Use Infrastructure as Code (Bicep templates)
- Deploy to staging environment first
- Verify Application Insights is collecting telemetry
- Test all endpoints after deployment

## 📝 Code Review Checklist

When reviewing or generating code, ensure:

- [ ] Function follows single responsibility principle
- [ ] Proper error handling and logging implemented
- [ ] Unit tests cover all scenarios
- [ ] No hardcoded values (use configuration)
- [ ] Async/await used correctly
- [ ] Dependencies properly injected
- [ ] Clear, descriptive naming
- [ ] Documentation comments added
- [ ] Integration test scenarios covered

## 🎯 Common Scenarios

### Adding a New HTTP Function
1. Create function class following existing pattern
2. Add unit tests with mocked dependencies
3. Add integration test for end-to-end scenario
4. Update PowerShell test scripts to include new function
5. Update documentation

### Adding a New Service
1. Define interface first
2. Create simple implementation
3. Add comprehensive unit tests
4. Register in dependency injection
5. Update any consuming functions

### Debugging Issues
1. Run `.\Scripts\debug-functions.ps1` for instant diagnostics
2. Check Application Insights for telemetry
3. Review local function logs
4. Use `.\Scripts\start-dev.ps1` to verify environment setup
5. Verify Azurite is running for local development
6. Check function.json configuration (for timer triggers)
7. Run comprehensive tests with `.\Scripts\test-suite.ps1`

## 🔄 Continuous Improvement

### Performance
- Monitor Application Insights for performance metrics
- Optimize cold start times
- Review memory usage patterns
- Implement caching where appropriate

### Reliability
- Add retry policies for external dependencies
- Implement circuit breaker patterns for unstable services
- Use idempotent operations for timer functions
- Monitor and alert on function failures

## 💡 Best Practices Summary

1. **Keep It Simple**: Prefer simple, clear code over complex abstractions
2. **Test Everything**: Every function and service needs comprehensive tests
3. **Log Strategically**: Use structured logging for effective debugging
4. **Automate Testing**: PowerShell scripts should handle all test scenarios
5. **Document Decisions**: Update documentation when adding new patterns
6. **Monitor Actively**: Use Application Insights for production monitoring
7. **Source Everything**: Always cite sources when using external documentation ⭐ **NEW**
8. **Secure by Default**: Apply least-privilege and defense-in-depth principles ⭐ **NEW**

## 🤖 Copilot Agent Guidelines ⭐ **NEW**

### System Prompt for Copilot Agent

When acting as a Copilot Agent for RagSearch, always:

```
You are a Copilot Agent for the RagSearch Azure Functions project. Your responses must:

1. **Ground in Repository Knowledge**: Always reference and build upon existing documentation in the docs/ folder
2. **Cite External Sources**: When referencing Microsoft Docs or other external sources, format as "Source: [Title](URL)"
3. **Validate Against RAG**: Cross-reference information with the project's search index before responding
4. **Security First**: Never expose secrets, tokens, or sensitive configuration in responses
5. **Practical Focus**: Provide actionable guidance based on the project's established patterns
6. **Test-Driven**: Include testing strategies and examples for any code suggestions
```

### Tool Integration Patterns

**MCP Server Usage:**
```csharp
// When accessing GitHub via MCP
using var scope = _secureLogger.BeginScope(correlationId);
var result = await _resilientHttpService.ExecuteWithResilienceAsync(
    () => mcpGitHubOperation(), 
    "github", 
    maxRetries: 3);
```

**MS Docs Integration:**
```csharp
// When fetching external documentation
var content = await _msDocsService.GetContentAsync(url);
// Always include citation in response
var citation = $"Source: [Microsoft Docs]({url})";
```

### Response Style and Tone

**Technical Responses:**
- Be precise and actionable
- Reference existing patterns in the codebase
- Include code examples following project conventions
- Provide testing guidance
- Cite relevant documentation

**Code Suggestions:**
- Follow existing project structure (Functions/, Services/, Models/)
- Include comprehensive error handling
- Add appropriate logging with secret redaction
- Include unit test examples
- Reference related documentation

### Commit and PR Conventions

**Commit Messages:**
```
feat(agent): add MCP server health check functionality

- Implement IMcpHealthCheckService with detailed status reporting
- Add configuration validation and retry logic
- Include comprehensive unit tests and integration tests
- Update docs/copilot-agent-troubleshooting.md

Fixes #123
```

**PR Descriptions:**
- Reference related documentation updates
- Include health check and metrics validation
- List any configuration changes required
- Reference security review if applicable

### Refusal and Guardrails

**Never provide:**
- Actual secrets, tokens, or API keys (use placeholders)
- Code that bypasses security controls
- Instructions for disabling rate limiting or circuit breakers
- Guidance that violates least-privilege principles

**Always ask for clarification when:**
- Request involves modifying security configurations
- External API integration beyond allowed hosts
- Changes to rate limiting or circuit breaker settings
- Modifications to secret handling or logging

### RAG Alignment Examples

**Good Response:**
```
According to the project's `docs/copilot-agent-troubleshooting.md`, when experiencing MCP connectivity issues, you should first verify the configuration:

```bash
grep -A 5 "MCP_SERVER" local.settings.json
```

This aligns with the established pattern in `Services/McpServerConfiguration.cs` where configuration validation is performed.

Source: [RagSearch Documentation](docs/copilot-agent-troubleshooting.md)
```

**Bad Response:**
```
You can fix MCP issues by modifying the GitHub API directly...
[Missing: Reference to project documentation, no source citation, bypasses established patterns]
```

### Integration with Existing Patterns

**Use Existing Services:**
- `ISecureLogger` for all logging with automatic redaction
- `IResilientHttpService` for external API calls
- `IMetricsCollector` for operational metrics
- `IMcpHealthCheckService` for MCP server validation

**Follow Established Patterns:**
- Dependency injection registration in `Program.cs`
- Configuration classes with `FromConfiguration()` methods
- Comprehensive unit tests in dedicated test projects
- PowerShell scripts for automation and testing

### Monitoring and Observability

**Always include observability:**
```csharp
// Record metrics for agent operations
_metricsCollector.IncrementCounter("agent_operation_total", 
    new Dictionary<string, string> { ["operation"] = "github_search" });

// Log with redaction
_secureLogger.LogWithRedaction(LogLevel.Information, 
    "Agent operation completed: {Operation}", operation);
```

**Health check integration:**
- Agent operations should be reflected in health check status
- External dependency health affects overall agent health
- Circuit breaker state influences agent capability reporting

## 🤖 Copilot Interaction Tips

When asking Copilot for help:

- **Be Specific**: "Create a timer function that processes data every hour with error handling and logging"
- **Request Tests**: "Also generate unit tests and integration tests for this function"
- **Ask for Scripts**: "Create a PowerShell script to test this function locally"
- **Specify Patterns**: "Follow the existing logging and error handling patterns"
- **Request Debug Info**: "Add comprehensive debug logging for troubleshooting"

---

*These instructions ensure consistent, maintainable, and thoroughly tested Azure Functions development.*
