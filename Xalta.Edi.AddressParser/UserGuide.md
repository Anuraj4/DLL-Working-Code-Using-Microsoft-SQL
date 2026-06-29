# Xalta Address Parser - User & Scaling Guide

## 🚀 Overview
The `Xalta.Edi.AddressParser` is an enterprise-grade library designed to decompose raw US address strings into structured components: **Address Line 1**, **City**, **State**, and **Zip**. 

It uses a dual-layer data strategy (External API + Local Excel Cache) to ensure accuracy while maximizing performance and minimizing network dependency.

---

## 🛠 Usage Guide

### 1. Basic Implementation
To use the parser, you need to initialize the data service which orchestrates the loading of state and city data.

```csharp
using Xalta.Edi.AddressParser;
using Xalta.Edi.AddressParser.Providers;
using Xalta.Edi.AddressParser.Services;
using Xalta.Edi.AddressParser.Parsers;

// 1. Setup Dependencies
var httpClient = new HttpClient();
var apiProvider = new CountriesNowApiProvider(httpClient);
var excelProvider = new ExcelAddressDataProvider("output/AddressData.xlsx");
var dataService = new AddressDataService(apiProvider, excelProvider);

// 2. Load Data (Checks Excel first, then API)
await dataService.InitializeAsync();

// 3. Parse Address
var parser = new UsAddressParser(dataService);
var result = parser.Parse("484 TEMPLE HILL RD STE 104, NEW WINDSOR, NY 12553-5529");

Console.WriteLine($"Address: {result.AddressLine1}"); // 484 TEMPLE HILL RD STE 104
Console.WriteLine($"City: {result.City}");           // New Windsor
Console.WriteLine($"State: {result.State}");         // NY
Console.WriteLine($"Zip: {result.Zip}");             // 12553-5529
```

### 2. Caching Mechanism
- **First Run**: The library detects that `AddressData.xlsx` is missing or empty. It calls the CountriesNow API to fetch all US states and cities, then saves them to the Excel file.
- **Subsequent Runs**: The library loads data directly from `AddressData.xlsx`, ensuring zero latency and zero API cost.

> [!TIP]
> **Preferred Storage Location**: To avoid storing data in the `bin` folder, you can pass the absolute path to your project's root `output` folder:
> `var excelProvider = new ExcelAddressDataProvider(@"C:\Users\sonup\OneDrive - Xalta Technology Services Pvt Ltd\Documents\Projects\EDI Fabric\Important Codes, Documents\FInal Library Code\EOB_TO_EDI_835\output\AddressData.xlsx");`

---


## 📈 How to Scale

The library is built on **SOLID principles** and the **Strategy Pattern**, making it highly customizable.

### 1. Adding New Countries
Currently, the focus is on the US. To scale to other countries:
- Update `InitializeAsync` in `AddressDataService` to accept a country name.
- The `CountriesNowApiProvider` is already generic and can fetch data for any country supported by the API.

### 2. Implementing New Data Providers
If you want to move from Excel to a SQL Database or a Redis Cache:
1. Create a new class implementing `IAddressDataProvider`.
2. Inject your new provider into `AddressDataService`.
3. No changes are required in the parsing logic itself.

### 3. Custom Parsing Logic
For specialized address formats (e.g., International addresses or specific EOB peculiarities):
1. Implementation a new `IAddressParser` (e.g., `UkAddressParser`).
2. Use the existing `AddressDataService` to feed it the necessary geographic data.

### 4. Advanced Regex & ML
The `UsAddressParser` uses robust Regex. For extremely complex scenarios, you can replace the Regex logic with an AI/ML-based model by simply swapping the `IAddressParser` implementation.

---

## 📐 Design Patterns Used
- **Dependency Injection**: All providers are injected, allowing for easy mocking and testing.
- **Repository Pattern**: `IAddressDataProvider` abstracts the source of truth (API/Excel).
- **Service Layer**: `AddressDataService` encapsulates the business logic of caching.
- **Interface Segregation**: Clients depend on narrow interfaces (`IAddressParser`), not concrete implementations.
