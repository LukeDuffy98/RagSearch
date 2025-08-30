# Test script to ingest a specific URL content into our SimplifiedSearchService
# This simulates adding the Microsoft Learn document about Copilot Security prompting tips

$content = @"
As with any Microsoft Copilot, a prompt refers to the text-based, natural language input you provide in the prompt bar that instructs Security Copilot to generate a response. The quality of the response that Security Copilot returns depends in large part on the quality of the prompt used. In general, a well-crafted prompt with clear and specific inputs leads to more useful responses by Security Copilot.

Elements of an effective prompt:
Effective prompts give Security Copilot adequate and useful parameters to generate a valuable response. Security analysts or researchers should include the following elements when writing a prompt:
• Goal - specific, security-related information that you need
• Context - why you need this information or how you plan to use it
• Expectations - format or target audience you want the response tailored to
• Source - known information, data sources, or plugins Security Copilot should use

Other prompting tips:
• Be specific, clear, and concise as much as you can about what you want to achieve. You can always start simply with your first prompt, but as you get more familiar with Security Copilot, include more details following the elements of an effective prompt.
• Iterate. Subsequent prompts are typically needed to either clarify what you need further, or try other versions of a prompt to get closer to what you're looking for.
• Provide necessary context to narrow down where Security Copilot looks for data.
• Give positive instructions instead of what not to do. Security Copilot is geared toward action, so telling it what you want it to do for exceptions is more productive.
• Directly address Security Copilot as You, as in, You should ... or You must ..., as this is more effective than referring to it as a model or assistant.
"@

Write-Host "Testing URL ingestion for: https://learn.microsoft.com/en-us/copilot/security/prompting-tips"
Write-Host "Content length: $($content.Length) characters"
Write-Host ""

# Test 1: Add the test documents first to ensure the service is working
Write-Host "Step 1: Adding test documents to verify service is working..."
try {
    $response = Invoke-RestMethod -Uri "http://localhost:7071/api/AddTestDocuments" -Method POST
    Write-Host "✅ Test documents added successfully: $($response.indexedDocuments)/$($response.totalDocuments)"
} catch {
    Write-Host "❌ Failed to add test documents: $_"
    exit 1
}

# Test 2: Search for existing content to verify search is working
Write-Host ""
Write-Host "Step 2: Testing search functionality..."
try {
    $searchResponse = Invoke-RestMethod -Uri "http://localhost:7071/api/Search?q=Azure&type=keyword"
    Write-Host "✅ Search working: Found $($searchResponse.totalResults) results for 'Azure'"
} catch {
    Write-Host "❌ Search failed: $_"
    exit 1
}

# Test 3: Search for content that would be in the Microsoft Learn document
Write-Host ""
Write-Host "Step 3: Searching for 'Security Copilot' (should not be found yet)..."
try {
    $searchResponse = Invoke-RestMethod -Uri "http://localhost:7071/api/Search?q=Security+Copilot&type=keyword"
    Write-Host "📊 Found $($searchResponse.totalResults) results for 'Security Copilot' (expected: 0)"
    
    if ($searchResponse.totalResults -gt 0) {
        Write-Host "⚠️  Security Copilot content already exists in index"
    } else {
        Write-Host "✅ Confirmed: Security Copilot content not yet in index"
    }
} catch {
    Write-Host "❌ Search for Security Copilot failed: $_"
}

Write-Host ""
Write-Host "🎯 Next step: We need to create a function to add the Microsoft Learn content"
Write-Host "   The content is ready to be indexed and contains $($content.Length) characters"
Write-Host "   URL: https://learn.microsoft.com/en-us/copilot/security/prompting-tips"
