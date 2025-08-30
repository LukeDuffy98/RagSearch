# RagSearch Azure Functions Project

## 🎯 Project Overview

**RagSearch** is a modern Azure Functions application built with C# and the .NET 8.0 isolated process model. This project serves as a foundation for building serverless applications that can handle various types of workloads including HTTP APIs, scheduled tasks, and event-driven processing.

## 🏗️ Architecture

The project follows Azure Functions best practices and modern .NET development patterns:

- **Runtime**: .NET 8.0 with isolated process model
- **Hosting**: Azure Functions v4 runtime
- **Storage**: Azure Storage (Azurite for local development)
- **Monitoring**: Application Insights integration
- **Infrastructure**: Infrastructure as Code using Bicep templates

## 🚀 What We've Built

This project demonstrates a **complete RAG (Retrieval-Augmented Generation) search solution** with vector embeddings and semantic search capabilities:

### Core Components
1. **HTTP-triggered Functions** - REST API endpoints for search operations and document indexing
2. **SimplifiedSearchService** - Function-native search with Azure OpenAI vector embeddings
3. **Vector Search Engine** - Semantic search using `text-embedding-3-small` model
4. **Hybrid Search** - Combines keyword and vector search with weighted scoring
5. **Persistent Storage** - Documents and embeddings stored in Azure Blob Storage
6. **URL Content Ingestion** - Real-time web content processing and indexing
7. **Testing Framework** - Comprehensive automated testing with detailed reporting
8. **Development Tools** - PowerShell tooling ecosystem for efficient development

### Key Features Implemented
- ✅ **Vector Embeddings**: Real Azure OpenAI integration with `text-embedding-3-small`
- ✅ **Semantic Search**: Find relevant content based on meaning, not just keywords
- ✅ **Hybrid Search**: 60% keyword + 40% vector scoring with result boosting
- ✅ **Persistent Storage**: All data survives restarts and deployments
- ✅ **Cost-Effective**: ~$10-50/month vs ~$260-300/month for Azure AI Search
- ✅ **Real-time Indexing**: URL content ingestion with immediate search availability
- ✅ **Performance Optimized**: In-memory caching with 5-minute refresh intervals
- ✅ **Production Ready**: Error handling, retry logic, and comprehensive monitoring
- ✅ **Dual Architecture**: Choice between SimplifiedSearch and Azure AI Search
- ✅ **Complete Testing**: Automated validation of all search types and capabilities

## 📁 Project Structure

```
RagSearch/
├── Services/                        # 🔍 Search Service Implementations
│   ├── SimplifiedSearchService.cs  # Vector search with Azure OpenAI embeddings
│   ├── AzureSearchService.cs       # Enterprise Azure AI Search service
│   └── ISearchService.cs           # Search service interface
├── Models/                          # 📋 Data Models
│   ├── SearchModels.cs             # Search request/response models
│   └── DocumentModels.cs           # Document processing models
├── Functions/                       # ⚡ Azure Functions
│   ├── SearchFunction.cs           # Search API with all search types
│   ├── IndexTestFunction.cs        # Document indexing and URL ingestion
│   └── HttpExample.cs              # Example HTTP function
├── docs/                           # 📚 Documentation
│   ├── overview.md                 # This file - project overview
│   ├── simplified-search-service.md # Detailed SimplifiedSearch implementation
│   ├── implementation-complete.md  # Complete implementation summary
│   ├── functions-reference.md      # Detailed function documentation
│   └── copilot-instructions.md     # GitHub Copilot development guidelines
├── Scripts/                        # 🛠️ Development & Testing Tools
│   ├── start-dev.ps1              # Complete dev environment manager
│   ├── debug-functions.ps1         # Quick debugging and diagnostics
│   └── test-suite.ps1              # Comprehensive testing framework
├── infra/                          # 🏗️ Infrastructure as Code
│   ├── main.bicep                  # Azure resources template
│   └── main.parameters.json        # Deployment parameters
├── HttpTriggerFunction.cs          # 🌐 HTTP API endpoint
├── TimerTriggerFunction.cs         # ⏰ Scheduled background task
├── Program.cs                      # 🚀 Application entry point
├── host.json                       # ⚙️ Azure Functions configuration
├── local.settings.json             # 🔧 Local development settings
├── RagSearch.csproj                # 📦 Project dependencies
├── azure.yaml                      # 🎯 Azure Developer CLI config
├── dev-helper.ps1                  # 🛠️ Legacy development helper
├── deploy.ps1                      # 🚀 Deployment script
└── README.md                       # 📖 Getting started guide
```