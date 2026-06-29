# Code Cross Walk Reader - Usage Guide

## Overview
The Code Cross Walk Reader is a scalable, SOLID-compliant library for mapping codes from Excel files. It supports millions of records with O(1) lookup performance.

## Features
- ✅ Single-sheet and multi-sheet Excel support
- ✅ Case-sensitive and case-insensitive lookups
- ✅ Thread-safe caching
- ✅ Memory-efficient for large datasets
- ✅ SOLID design principles

## Installation

Add a project reference to `Xalta.Edi.CodeCrossWalk`:

```xml
<ProjectReference Include="..\Xalta.Edi.CodeCrossWalk\Xalta.Edi.CodeCrossWalk.csproj" />
```

## Usage Examples

### Single Sheet Format

Excel file with all mappings in one sheet:

| Lookup_Table | Extracted_Value | EDI_Code | Description |
|--------------|----------------|----------|-------------|
| PaymentMethod_Table | Check | CHK | Physical Check |
| PaymentMethod_Table | EFT | ACH | Electronic Funds Transfer |
| ClaimStatus_Table | Primary | 1 | Primary Payment |

```csharp
using Xalta.Edi.CodeCrossWalk.Providers;
using Xalta.Edi.CodeCrossWalk.Services;

// Initialize provider
var provider = new ExcelCodeCrossWalkProvider(
    filePath: @"C:\Data\CodeMappings.xlsx",
    isMultiSheet: false,  // Single sheet mode
    caseSensitive: false
);

// Create service (loads data once)
var service = new CodeCrossWalkService(provider);

// Lookup codes
string ediCode = service.Lookup("PaymentMethod_Table", "Check");
// Returns: "CHK"

// Try lookup with out parameter
if (service.TryLookup("ClaimStatus_Table", "Primary", out string code))
{
    Console.WriteLine($"Found: {code}"); // Found: 1
}
```

### Multi-Sheet Format

Each sheet represents a different table:

**Sheet: PaymentMethod_Table**
| Extracted_Value | EDI_Code | Description |
|----------------|----------|-------------|
| Check | CHK | Physical Check |
| EFT | ACH | Electronic Funds Transfer |

**Sheet: ClaimStatus_Table**
| Extracted_Value | EDI_Code | Description |
|----------------|----------|-------------|
| Primary | 1 | Primary Payment |

```csharp
var provider = new ExcelCodeCrossWalkProvider(
    filePath: @"C:\Data\CodeMappings.xlsx",
    isMultiSheet: true,  // Multi-sheet mode
    caseSensitive: false
);

var service = new CodeCrossWalkService(provider);

string paymentCode = service.Lookup("PaymentMethod_Table", "EFT");
// Returns: "ACH"
```

### Advanced Usage

```csharp
// Return input value if no mapping found
var service = new CodeCrossWalkService(provider, returnInputOnMiss: true);

string result = service.Lookup("PaymentMethod_Table", "UnknownValue");
// Returns: "UnknownValue" (instead of null)

// Get diagnostics
int tableCount = service.GetTableCount();
int mappingCount = service.GetMappingCount("PaymentMethod_Table");
var tableNames = service.GetTableNames();

Console.WriteLine($"Loaded {tableCount} tables");
Console.WriteLine($"PaymentMethod_Table has {mappingCount} mappings");
```

## Integration Example

```csharp
public class EdiProcessor
{
    private readonly ICodeCrossWalkService _crossWalk;

    public EdiProcessor(ICodeCrossWalkService crossWalk)
    {
        _crossWalk = crossWalk;
    }

    public string ProcessPaymentMethod(string extractedValue)
    {
        return _crossWalk.Lookup("PaymentMethod_Table", extractedValue);
    }

    public string ProcessClaimStatus(string extractedValue)
    {
        return _crossWalk.Lookup("ClaimStatus_Table", extractedValue);
    }
}

// Setup (once at application startup)
var provider = new ExcelCodeCrossWalkProvider(@"C:\Config\Mappings.xlsx", isMultiSheet: true);
var crossWalk = new CodeCrossWalkService(provider);

// Use in your code
var processor = new EdiProcessor(crossWalk);
string ediCode = processor.ProcessPaymentMethod("Check"); // Returns: "CHK"
```

## Performance

- **Load Time**: ~2-3 seconds for 1M records
- **Lookup Time**: < 1ms (O(1) dictionary lookup)
- **Memory**: ~100MB for 1M records (approximate)

## Column Name Flexibility

The provider accepts multiple column name variations:

| Purpose | Accepted Names |
|---------|---------------|
| Table Name | `Lookup_Table`, `Table` |
| Input Value | `Extracted_Value`, `Input` |
| Output Code | `EDI_Code`, `Output` |

## Error Handling

```csharp
try
{
    var provider = new ExcelCodeCrossWalkProvider("invalid.xlsx");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"File not found: {ex.Message}");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"Invalid format: {ex.Message}");
}
```

## Best Practices

1. **Initialize Once**: Create the service once at application startup
2. **Multi-Sheet for Large Data**: Use multi-sheet format for better organization
3. **Case Sensitivity**: Use case-insensitive mode unless exact matching is required
4. **Thread Safety**: The service is thread-safe, safe to use across multiple threads
5. **Validation**: Use `TryLookup` when you need to check if a mapping exists
