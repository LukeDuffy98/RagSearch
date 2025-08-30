# GitHub Copilot Chat Commands for RagSearch

This file contains specific commands and prompts to use with GitHub Copilot Chat for efficient development on the RagSearch Azure Functions project.

## 🚀 Quick Commands

### Function Development
```
@workspace Create a new HTTP Azure Function following the RagSearch pattern with logging, error handling, and unit tests
```

```
@workspace Add a timer trigger function that runs daily at 2 AM, include comprehensive logging and tests
```

```
@workspace Generate a blob trigger function that processes uploaded files, include error handling and debug logging
```

### Testing Commands
```
@workspace Create unit tests for the HttpTriggerFunction class using MSTest and Moq
```

```
@workspace Generate integration tests for all existing functions with proper mocking
```

```
@workspace Run the comprehensive test suite: .\Scripts\test-suite.ps1 -TestType All -GenerateReport
```

```
@workspace Create additional test scenarios for the PowerShell testing framework
```

### Debugging Commands
```
@workspace Help me debug issues using: .\Scripts\debug-functions.ps1 -DetailedOutput
```

```
@workspace Start the development environment: .\Scripts\start-dev.ps1
```

```
@workspace Run diagnostics and check service status with our comprehensive debugging tools
```

```
@workspace Create debug logging for the HTTP function to troubleshoot request processing
```

```
@workspace Generate additional PowerShell debugging scripts following our established patterns
```

### Infrastructure Commands
```
@workspace Update the Bicep template to add a new Azure service following best practices
```

```
@workspace Create deployment scripts with error handling and validation
```

## 🎯 Specific Development Scenarios

### Scenario 1: Adding New HTTP Endpoint
**Prompt**: 
```
I need to create a new HTTP function called "ProcessData" that:
- Accepts POST requests with JSON body
- Validates the input data
- Processes it asynchronously  
- Returns appropriate HTTP status codes
- Includes comprehensive logging
- Follows the existing RagSearch patterns
- Comes with complete unit and integration tests
```

### Scenario 2: Creating Background Processing
**Prompt**:
```
Create a timer function that:
- Runs every 30 minutes
- Processes pending items from a queue
- Includes retry logic for failures
- Logs detailed execution information
- Has configurable schedule via app settings
- Includes comprehensive error handling
- Comes with unit tests and PowerShell test script
```

### Scenario 3: Adding External Service Integration
**Prompt**:
```
Create a service class that:
- Integrates with an external REST API
- Uses HttpClient with proper disposal
- Implements retry policies
- Includes comprehensive error handling
- Uses structured logging
- Is testable with unit tests
- Follows dependency injection patterns
```

### Scenario 4: Troubleshooting and Debugging
**Prompt**:
```
My timer function is failing to start. Help me:
- Add detailed debug logging to identify the issue
- Create a PowerShell script to test the timer trigger
- Generate diagnostic information about storage connections
- Provide troubleshooting steps with specific log messages to look for
```

## 🛠️ Development Tools Commands

### Start Development Environment
```
@workspace Start all services for development: .\Scripts\start-dev.ps1
```

```
@workspace Stop all services: .\Scripts\start-dev.ps1 -StopFirst
```

```
@workspace View recent service logs: .\Scripts\start-dev.ps1 -LogsOnly
```

### Debug and Diagnostics
```
@workspace Run quick system health check: .\Scripts\debug-functions.ps1
```

```
@workspace Get detailed system diagnostics: .\Scripts\debug-functions.ps1 -DetailedOutput
```

```
@workspace Test specific function: .\Scripts\debug-functions.ps1 -FunctionName "HttpExample"
```

```
@workspace Start services automatically: .\Scripts\debug-functions.ps1 -StartServices
```

### Comprehensive Testing Framework
```
@workspace Run all tests with report: .\Scripts\test-suite.ps1 -TestType All -GenerateReport
```

```
@workspace Run HTTP function tests: .\Scripts\test-suite.ps1 -TestType Http -DetailedOutput
```

```
@workspace Run debug diagnostics: .\Scripts\test-suite.ps1 -TestType Debug
```

```
@workspace Run integration tests: .\Scripts\test-suite.ps1 -TestType Integration
```

```
@workspace Run local environment tests: .\Scripts\test-suite.ps1 -TestType Local
```

### Legacy Development Helper
```
@workspace Use dev-helper for Azurite: .\dev-helper.ps1 -StartAzurite
```

```
@workspace Use dev-helper for Functions: .\dev-helper.ps1 -StartFunctions
```

```
@workspace Build and start all: .\dev-helper.ps1 -Build -StartAll
```

## 🧪 Testing Commands

### Unit Testing
```
@workspace Generate comprehensive unit tests for [FunctionName] including edge cases and error scenarios
```

```
@workspace Create mock objects for all dependencies in [ServiceName] and write complete test coverage
```

### Integration Testing
```
@workspace Create end-to-end integration tests for the HTTP function including request/response validation
```

```
@workspace Generate integration tests that verify timer function execution with real storage emulator
```

### PowerShell Testing Scripts
```
@workspace Extend the existing test-suite.ps1 with additional test types and scenarios
```

```
@workspace Create specialized testing scripts based on our established PowerShell testing framework
```

```
@workspace Generate performance testing scripts using our existing debug and testing infrastructure
```

```
@workspace Add new test scenarios to: .\Scripts\test-suite.ps1 -TestType [NewType]
```

## 🔧 Maintenance Commands

### Code Quality
```
@workspace Review the current codebase and suggest improvements for maintainability and performance
```

```
@workspace Analyze the project structure and recommend optimizations following Azure Functions best practices
```

### Documentation
```
@workspace Update the functions-reference.md file to include the new [FunctionName] function with complete documentation
```

```
@workspace Generate API documentation for all HTTP functions including request/response examples
```

### Performance
```
@workspace Analyze the current functions for performance bottlenecks and suggest optimizations
```

```
@workspace Add performance monitoring and metrics collection to existing functions
```

## 🚨 Troubleshooting Commands

### Common Issues
```
@workspace The Azure Functions are not starting locally. Use .\Scripts\debug-functions.ps1 -StartServices to diagnose and fix.
```

```
@workspace Timer functions are failing with storage connection errors. Use .\Scripts\start-dev.ps1 to ensure Azurite is running.
```

```
@workspace HTTP functions are returning 500 errors. Run .\Scripts\test-suite.ps1 -TestType Http -DetailedOutput for diagnosis.
```

```
@workspace Services won't start: Use .\Scripts\start-dev.ps1 -StopFirst then .\Scripts\start-dev.ps1 for clean restart.
```

### Diagnostic Commands
```
@workspace Use our comprehensive diagnostic tools: .\Scripts\debug-functions.ps1 -DetailedOutput
```

```
@workspace Run full system validation: .\Scripts\test-suite.ps1 -TestType Debug -GenerateReport
```

```
@workspace Check service logs: .\Scripts\start-dev.ps1 -LogsOnly
```

```
@workspace Generate additional diagnostic scripts following our established PowerShell patterns
```

## 📊 Monitoring and Logging

### Application Insights
```
@workspace Add Application Insights custom telemetry to track business metrics in existing functions
```

```
@workspace Create dashboard queries for monitoring function performance and error rates
```

### Custom Logging
```
@workspace Enhance existing functions with structured logging that includes correlation IDs and timing information
```

```
@workspace Add performance counters and custom metrics to track function execution patterns
```

## 🔄 DevOps Integration

### CI/CD Pipeline
```
@workspace Create GitHub Actions workflow for automated testing and deployment of Azure Functions
```

```
@workspace Generate build and deployment scripts with comprehensive validation and rollback capabilities
```

### Environment Management
```
@workspace Create configuration management for multiple environments (dev, staging, prod) with validation
```

```
@workspace Generate environment-specific settings and deployment parameters with security best practices
```

## 💡 Advanced Scenarios

### Microservices Pattern
```
@workspace Design a microservices architecture using Azure Functions with service-to-service communication
```

### Event-Driven Architecture
```
@workspace Create an event-driven workflow using multiple function triggers (HTTP, Queue, Blob, Timer)
```

### Data Processing Pipeline
```
@workspace Design a data processing pipeline with functions that handle ingestion, transformation, and storage
```

## 🎭 Response Format Preferences

When requesting code from Copilot, specify:

```
Please provide:
1. Complete, working code with error handling
2. Comprehensive unit tests using MSTest and Moq
3. PowerShell script for testing the functionality
4. Detailed logging with structured information
5. Documentation comments explaining the implementation
6. Integration test examples
7. Troubleshooting guide for common issues
```

## 🔍 Code Review Prompts

```
@workspace Review this function for Azure Functions best practices, security concerns, and performance optimizations
```

```
@workspace Analyze this code for potential issues and suggest improvements following the RagSearch project patterns
```

```
@workspace Check this implementation for proper error handling, logging, and testability
```

---

*Use these commands and prompts to efficiently develop, test, and maintain the RagSearch Azure Functions project with GitHub Copilot assistance.*
