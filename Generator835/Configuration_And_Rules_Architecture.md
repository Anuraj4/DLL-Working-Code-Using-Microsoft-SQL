# EDI 835 Generator - Configuration & Rules Architecture

This document provides a comprehensive overview of how the EDI 835 Generator loads, manages, and applies configuration values from the various Excel sheets in the `updated_config` workbook. It details how these settings dictate document mapping, processing rules, and fallback defaults dynamically.

---

## 1. Top-Level Bootstrapping & The Central Configuration Hub

At the core of the system is the `MappingConfiguration` class, populated dynamically by the `ExcelMappingProvider` and `MappingLoader`.

### The Master Engine: `general`
*   **Purpose:** The `general` sheet acts as the foundational blueprint for the entire configuration engine.
*   **Mechanism:** When the application starts, it reads *only* the `general` sheet first. This sheet explicitly lists down the names of every other sheet that should be loaded (the active mapping sheets). The system iteratively loads the other sheets (skipping any explicitly blacklisted ones like `Column_Name_To_AMT01`) dynamically based on this inventory.
*   **Storage:** All loaded tables are stored within `MappingConfiguration`. Specialized sheets get specific properties (like `PayerRegistry` or `MappedSettings`), while all dynamically listed sheets are preserved fully within `RawMappingTables` for flexible crosswalk queries.

---

## 2. Configuration Sheets Breakdown

The configuration is driven by 17 specialized sheets (managed by the `general` loader). Here is how they interact and regulate processing:

### A. Pre-Processing & Data Normalization
These sheets operate before the EDI rules engine engages, bridging the gap between raw optical/CSV extracts and the standardized domain models.

*   **`csv_mapper` (The Transformation Map)**
    *   **Purpose:** Normalizes incoming CSV data into strict categorical buckets (`payment_header`, `claims`, `service_lines`, etc.).
    *   **How it works:** It matches columns in the CSV (e.g., "Amount Billed") to the expected TargetSheet and TargetColumn. Used heavily by the `CsvToExcelMapper` phase.
*   **`payer_registry` (The Payer Anchor)**
    *   **Purpose:** Identifies exactly "who" the payer is. Maps fuzzy extracted names or EIN/TIN combinations into a rigid `Payer ID`. 
    *   **Significance:** Every subsequent payer-specific override relies on the `Payer ID` found in this registry step.

### B. Envelope & Structure Governance
These sheets dictate how the X12 file itself is structured and routed.

*   **`edi_settings`**
    *   **Purpose:** Controls high-level EDI envelope formatting (ISA and GS segments).
    *   **Lookup Hierarchy:** Checks active `Payer ID` -> falls back to `Fallback` row -> checks `common_settings`.
*   **`835_default_code` (The Absolute Constants)**
    *   **Purpose:** Supplies fixed, boilerplate strings defining the X12 835 standard (e.g., `ISA_UsageIndicator` = `T`). Payer-agnostic.
*   **`common_settings`**
    *   **Purpose:** Environment-level configurations, date formats, string separators, and API URLs needed across the generator.

### C. Financial & Banking Governance
*   **`default_payment_settings`**
    *   **Purpose:** Provides financial routing and banking information for the BPR segment (`BPR06` through `BPR15`).
    *   **Lookup Hierarchy:** Active `Payer ID` -> `Fallback` row.
*   **`currency_map`**
    *   **Purpose:** Maps extracted symbols (e.g., `$`, `€`) to official X12 currency code strings (e.g., `USD`, `EUR`) in the `Edi835Pipeline`.

### D. Dynamic Code Crosswalking
Instead of simple static fallbacks, these tables provide logical matrices to translate variable descriptive text from the EOB into strict 835 elements.

*   **`svc_qualifier_patterns`**
    *   **Purpose:** Used by the `Svc01QualifierRule`. Translates service/procedure codes into SVC01-01 qualifiers using Regular Expressions (e.g., 5 digits -> `HC` (CPT Category I), 11 digits -> `N4` (NDC)).
*   **`adjustment_group_mapping`**
    *   **Purpose:** High-priority crosswalk table resolving exact Claim Adjustment Group Codes (CAGC) and Reason Codes (CARC). It checks combinations of the extracted Payer Name/Type and fuzzy strings (like `"CO 45"`) to enforce correct accounting balances.
*   **`carc_cagc_mapping`**
    *   **Purpose:** Supplemental or master crosswalk linking standard CARC to CAGC for routine adjustments.
*   **`payment_method_mapping` & `claim_status_mapping` & `claim_filling_indicator`**
    *   **Purpose:** Simple translation dictionaries utilized within individual formatting rules to convert conversational EOB terms (e.g., "Wire", "Denied", "Medicare Part B") into standard alphanumeric EDI codes (`FWT`, `4`, `MB`).

### E. The Final Safety Nets
*   **`fallback_codes` (The elemental Safety Net)**
    *   **Purpose:** Supplies default values for specific transactional elements within the 835 loops (e.g., `CLP08`, `SVC05`, `LQ01`). 
    *   **Lookup Hierarchy:** Grouped by `Field Name`. Scans for active `Payer ID` in group -> drops down to `Fallback` in group.

---

## 3. Interaction with the Rules Engine

Sheets supply data, but C# code logic (`IRuleDefinition` / Pipeline classes) dictates execution. 

### Priority Order
When generating a node like `SVC01-01` or `BPR01`, the system executes in the following strict priority cascade:

1.  **Raw Input Data:** If the source payload somehow provided an explicit valid code, it attempts to map it directly.
2.  **Crosswalk Lookups (`RawMappingTables`):** For fields like Currency or Adjustments, it references dynamic arrays from sheets like `currency_map` or `adjustment_group_mapping` to attempt translation of the raw text.
3.  **Business Rule Engine (`IRuleDefinition`):**
    *   The engine evaluates dynamic context (e.g., `Svc01QualifierRule` searches the `svc_qualifier_patterns` regexes to deduce standard qualifiers dynamically based on the code shape). 
    *   Another example is `TransactionHandlingRule` enforcing zero-payment conditions to inject `"H"` regardless of standard routing.
4.  **Payer-Specific Configuration:** Drop down to the overarching spreadsheets (`edi_settings`, `default_payment_settings`, or `fallback_codes`) searching for an override attached specifically to the `MatchedPayer` (derived from `payer_registry`).
5.  **Generic Configuration (`Fallback`):** Grab the generic `Fallback` row for the requested element in those same grouped tables.
6.  **Global Constants:** Fall back to the absolute standards encoded in `835_default_code` or `common_settings`. 

This layered architecture elegantly merges strict X12 rules, enterprise-specific overrides, and regex-powered inference, all completely administered through Excel files without modifying code.
