# Claim-Level Sequestration Processing: Complete Analysis

## Overview
The codebase implements a sophisticated multi-phase system to detect and resolve **claim-level sequestration** — federal payment reductions that are applied uniformly across all service lines of a claim (typically 2% under Medicare sequestration rules).

---

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   EDI 835 Pipeline                          │
│         (Edi835Pipeline.cs)                                 │
└────────────────────┬────────────────────────────────────────┘
                     │
         Step 3.2: Math Balancing Phase
                     │
         ┌───────────┴──────────┬──────────────────┐
         │                      │                  │
    PreProcessClaimSequestration  BalanceServiceLine  (per line)
    (MathBalancingRule.cs)        (MathBalancingRule.cs)
         │
    ┌────┴────────────────────┐
    │  SequestrationService   │
    │  - ProcessClaim()       │
    └────────────────────┬────┘
         │
    ┌────┴────────────────────────┐
    │ 4-Step Detection/Resolution │
    │                             │
    │ 1. Detect Level             │
    │ 2. Confirm Status           │
    │ 3. Resolve Distribution     │
    │ 4. Sanitize Amounts         │
    └─────────────────────────────┘
```

---

## **STEP-BY-STEP PROCESSING FLOW**

### **Phase 1: Pipeline Initialization & Data Reading**
**File:** `Edi835Pipeline.cs` (Lines 1-120)
**Code Reference:** `Execute()` method

1. **Input Reading**
   - Reads Excel file containing EOB (Explanation of Benefits) data
   - Normalizes extracted data using `DataNormalizationService`
   - Synchronizes claim/line context

2. **Pre-processing Steps**
   - Fix patient responsibility amounts: `PatientResponsibilityFixerService.FixPatientResponsibility()`
   - Detect swapped Allowed/Adjustment amounts: `SwapDetectionService`

---

### **Phase 2: Pre-Process Claim Sequestration Detection**
**File:** `MathBalancingRule.cs` (Lines 63-150)
**Method:** `PreProcessClaimSequestration(ClaimData claim)`

This is the **first point of contact** for claim-level sequestration detection.

#### **Step 2.1: Gather Sequestration Data**
```csharp
var lineSeqData = allLines
    .Select(l => new
    {
        Line = l,
        SeqAmount = l.Adjustments.Sum(a => a.SequestrationAmount ?? 0m),
        HasCo253 = l.Adjustments.Any(a => a.AdjustmentReasonCode == "253")
    })
    .Where(x => x.SeqAmount > 0)
    .ToList();
```
- **Collects:** All service lines with non-zero sequestration amounts
- **Sources:** 
  - Column: `SequestrationAmount` (from Excel mapping)
  - Code: `CO-253` (Claim Adjustment Reason Code)

#### **Step 2.2: Identify Identical Amounts (Claim-Level Indicator)**
```csharp
bool allIdentical = lineSeqData.All(x => x.SeqAmount == firstAmount);
```
- **Key Check:** Are ALL sequestration amounts identical?
- **If YES:** Indicates claim-level sequestration
- **If NO:** Indicates service-line level sequestration

#### **Step 2.3: Confirm Claim-Level Status**
```csharp
if (allIdentical && !anyHasCo253)
{
    // Claim-level confirmed - proceed to distribution
}
```
- **Requirement 1:** All amounts must be identical
- **Requirement 2:** CO-253 code NOT already present (avoid double-processing)

#### **Step 2.4: Distribution Strategy - 5-Method Candidate System**
For each non-zero paid line, calculate THREE distribution candidates:

```csharp
// Method 1: Reverse Engineering (Paid / 0.98) * 0.02
decimal c1 = Math.Round(paid / 0.98m * 0.02m, 2);

// Method 2: Ceiling of 2% of Paid
decimal c2 = Math.Round(Math.Ceiling(paid * 0.02m * 100m) / 100m, 2);

// Method 3: Direct 2% of Paid
decimal c3 = Math.Round(paid * 0.02m, 2);

// Selection: Choose closest to the 2% target
decimal target2Percent = paid * 0.02m;
decimal best = c1;
if (Math.Abs(c2 - target2Percent) < Math.Abs(best - target2Percent)) best = c2;
if (Math.Abs(c3 - target2Percent) < Math.Abs(best - target2Percent)) best = c3;
```

#### **Step 2.5: Calculate Distribution Sum & Residue**
```csharp
decimal distributedSum = distributions.Sum();
decimal difference = totalToDistribute - distributedSum;
```
- **Scenario:** Distributing $100 claim-level seq across 3 lines
  - Line 1 (Paid $1000): $20.20
  - Line 2 (Paid $2000): $40.40
  - Line 3 (Paid $1000): $20.20
  - **Total Distributed:** $80.80
  - **Residue:** $100 - $80.80 = $19.20

#### **Step 2.6: Residue Distribution (Equal Distribution)**
```csharp
decimal adjustmentPerLine = Math.Truncate((difference / count) * 100) / 100;
decimal remainder = difference - (adjustmentPerLine * count);

for (int i = 0; i < count; i++) 
    distributions[i] += adjustmentPerLine;
distributions[count - 1] += remainder;  // Add remaining to last line
```
- **Example:** Residue $19.20 across 3 lines
  - Per line: $6.40
  - Line 1: $20.20 + $6.40 = $26.60
  - Line 2: $40.40 + $6.40 = $46.80
  - Line 3: $20.20 + $6.40 + $0 = $26.60 (no remainder here if balanced)

#### **Step 2.7: Apply Distributed Amounts**
```csharp
foreach (var d in lineSeqData)
{
    foreach (var adj in d.Line.Adjustments) 
        adj.SequestrationAmount = 0;  // Clear old values
}

for (int i = 0; i < nonZeroPaidLines.Count; i++)
{
    var line = nonZeroPaidLines[i];
    // Set adjustment to distributed amount with CO-253 code
    line.Adjustments[...].SequestrationAmount = distributions[i];
}
```

---

### **Phase 3: Advanced Sequestration Service Processing**
**File:** `SequestrationService.cs`
**Method:** `ProcessClaim(ClaimData claim, HeaderData header)`

This service is called AFTER MathBalancingRule and handles more complex scenarios.

#### **Step 3.1: Detect Sequestration Level (Service-Line vs Claim-Level)**
**Method:** `DetectSequestrationLevel(ClaimData claim, out decimal identicalAmount)`

```csharp
var seqAmounts = new List<decimal>();
foreach (var line in claim.ServiceLines)
{
    decimal lineSeq = GetExcelSequestration(line);
    if (lineSeq != 0) seqAmounts.Add(lineSeq);
}

if (seqAmounts.Count <= 1)
    return SequestrationLevel.ServiceLine;

decimal firstAmount = seqAmounts[0];
bool allSame = seqAmounts.All(a => Math.Abs(a - firstAmount) <= 0.001m);

if (allSame)
{
    identicalAmount = firstAmount;
    return SequestrationLevel.ClaimLevel;
}
```

**Outputs:**
- `SequestrationLevel.ServiceLine` → Each line has different amounts
- `SequestrationLevel.ClaimLevel` → All lines have same amount (within $0.001 tolerance)
- `identicalAmount` → The uniform amount (if claim-level)

#### **Step 3.2: Confirm Service-Line Sequestration (Guard Check)**
**Method:** `ConfirmServiceLineSequestration(ServiceLineData line, decimal excelSequestration, bool isMedicare)`

Even if amounts are identical, check if ANY line's sequestration matches its individual service-line calculation:

```csharp
var candidates = GetAllCandidates(line);  // 5-method candidates
if (candidates.Any(c => Math.Abs(c - excelSequestration) <= 0.01m)) 
    return true;  // This line justifies itself → it's service-line level

// Medicare-specific check
if (isMedicare)
{
    decimal? rev = CalculateSequestrationAmount(line.LinePaidAmount);
    if (rev.HasValue && Math.Abs(rev.Value - excelSequestration) <= 0.01m) 
        return true;
}
```

**Logic:**
- If ANY service line can prove its sequestration mathematically → Treat entire claim as service-line level
- Example: Claim has 3 lines with $10 sequestration each
  - But Line 2 has Paid = $500, and $10 ≈ 2% of $500
  - → Line 2 validates itself → CLAIM IS SERVICE-LINE LEVEL (despite identical amounts)

#### **Step 3.3: Fallback to Service-Line Handling**
**Method:** `HandleServiceLineFallback(ClaimData claim, HeaderData header)`

If service-line confirmation succeeds, fall back to per-line calculations:

```csharp
foreach (var line in claim.ServiceLines)
{
    decimal excelSeq = GetExcelSequestration(line);
    
    if (excelSeq == 0 && HasSequestrationCode(line))
    {
        // Line has CO-253 code but no amount
        decimal? bestCalc = CalculateBestSequestrationCandidate(line, isMedicare);
        if (bestCalc.HasValue && bestCalc.Value > 0)
        {
            ApplySequestrationToLine(line, bestCalc.Value);
        }
    }
}
```

#### **Step 3.4: Resolve Claim-Level Sequestration (Main Resolution)**
**Method:** `ResolveClaimLevelSequestration(ClaimData claim, decimal totalClaimSeq, HeaderData header)`

Only called if claim-level is confirmed. Distributes total claim sequestration per line:

```csharp
var results = new Dictionary<string, decimal>();
var co253Lines = claim.ServiceLines.Where(HasSequestrationCode).ToList();

if (co253Lines.Count > 0)
{
    decimal runningSum = 0m;
    foreach (var line in co253Lines)
    {
        // Calculate best amount for this line
        decimal bestCalc = CalculateBestSequestrationCandidate(line, isMedicare) ?? 0m;
        results[line.ServiceLineId] = bestCalc;
        runningSum += bestCalc;
    }

    // Distribute residue to last line
    decimal totalRem = totalClaimSeq - runningSum;
    if (Math.Abs(totalRem) > 0)
    {
        var lastKey = co253Lines.Last().ServiceLineId;
        results[lastKey] += totalRem;
    }
}
```

**Distribution Strategy:**
- **Primary:** Lines with CO-253 code (highest priority)
- **Fallback 1:** Last line with identical sequestration amount (matching the claim total)
- **Fallback 2:** First service line

---

### **Phase 4: Candidate Selection Strategy (5-Methods)**
**File:** `SequestrationService.cs`
**Method:** `GetAllCandidates(ServiceLineData line)`

For each service line, generates 5 candidate sequestration amounts:

#### **Method A: Medicare Reverse Engineering**
```csharp
decimal? methodA = CalculateSequestrationAmount(paid);
// Formula: paid = (original - seq)
//          original = paid / 0.98
//          seq = original * 0.02
```
**When Used:** Medicare claims, reverse-calculated from paid amount

#### **Method B: CMS Payment Base**
```csharp
decimal medicareBase = allowed - patientResponsibility;
decimal methodB = Math.Round(medicareBase * 0.02m, 2);
```
**When Used:** Medicare-specific, accounts for PR deductions

#### **Method C: Direct Paid Percentage**
```csharp
decimal methodC = Math.Round(paid * 0.02m, 2);
```
**When Used:** Simplest calculation, 2% of what was paid

#### **Method D: Standard Allowed Percentage**
```csharp
decimal methodD = Math.Round(allowed * 0.02m, 2);
```
**When Used:** Common for non-Medicare payers

#### **Method E: Gap Logic**
```csharp
decimal gap = allowed - paid - otherAdjustments;
decimal methodE = Math.Round(gap, 2);
```
**When Used:** When charge-based calculations don't match 2% logic

#### **Selection Algorithm: CalculateBestSequestrationCandidate()**
```csharp
if (isMedicare)
{
    // Priority: Reverse Engineering or CMS Base (Allowed-PR) * 0.02
    decimal medicareBase = GetMedicarePaymentBase(line);
    target = methodARev ?? Math.Round(medicareBase * 0.02m, 2);
}
else
{
    // Priority: Reverse Engineering if near 2% of Allowed
    // Otherwise: Gap Logic
    if (methodARev.HasValue && 
        Math.Abs(methodARev.Value - allowed2Percent) <= 0.05m)
        target = methodARev.Value;
    else
        target = gap;
}

// Return candidate closest to target
return candidates.OrderBy(c => Math.Abs(c - target)).FirstOrDefault();
```

**Priority Logic:**
- **Medicare:** Reverse Engineering or CMS Base (weighted 50/50)
- **Non-Medicare:** 
  - If Reverse Engineering ≈ 2% of Allowed (within $0.05): Use Reverse Engineering
  - Otherwise: Use Gap Logic

---

### **Phase 5: Final Sanitization & Application**
**File:** `SequestrationService.cs`
**Method:** `SanitizeSequestrationAmounts(ClaimData claim, Dictionary<string, decimal> resolvedAmounts)`

```csharp
foreach (var line in claim.ServiceLines)
{
    string key = line.ServiceLineId ?? line.CptCode ?? "";
    if (resolvedAmounts.TryGetValue(key, out decimal correct))
    {
        bool done = false;
        foreach (var adj in line.Adjustments)
        {
            // Find and update existing CO-253 or sequestration adjustment
            if (adj.AdjustmentReasonCode == "253" || 
                (adj.SequestrationAmount ?? 0m) != 0m)
            {
                if (!done)
                {
                    adj.SequestrationAmount = correct;
                    adj.AdjustmentAmount = correct;
                    done = true;
                }
                else
                {
                    // Zero out duplicates
                    adj.SequestrationAmount = 0m;
                    adj.AdjustmentAmount = 0m;
                }
            }
        }
        
        // Create new adjustment if none found
        if (!done && correct != 0)
        {
            ApplySequestrationToLine(line, correct);
        }
    }
}
```

---

## **Detection Service: Secondary Validation**
**File:** `SequestrationDetectionService.cs`
**Method:** `ProcessSequestration(Edi835DataModel model, MappingConfiguration mappings)`

Runs parallel detection and deduplication logic:

#### **Step 1: Group by Amount**
```csharp
var groupedAmounts = seqLines.GroupBy(x => x.Amount).ToList();
```

#### **Step 2: For Each Group, Categorize Lines**
```csharp
foreach (var group in groupedAmounts)
{
    decimal amount = group.Key;
    var verifiedLines = new List<ServiceLineData>();
    var unverifiedLines = new List<ServiceLineData>();
    
    foreach (var x in group)
    {
        if (IsServiceLevelSequestration(x.Line, amount))
            verifiedLines.Add(x.Line);
        else
            unverifiedLines.Add(x.Line);
    }
}
```

#### **Step 3: Apply Logic Based on Verification**

**Case 1: Some Lines Verified (Service-Level Validation)**
```csharp
if (verifiedLines.Any())
{
    // Keep verified lines
    // Clear unverified duplicates
}
```

**Case 2: No Verification + Multiple Identical Amounts (Claim-Level)**
```csharp
else if (lines.Count > 1)
{
    // This is claim-level duplication
    // Keep ONE representative (prefer one with CO-253 code)
    // Clear all others
}
```

**Case 3: Single Occurrence**
```csharp
else
{
    // Keep single line (usually safe)
}
```

---

## **Medicare Detection**
**Method:** `IsMedicare(HeaderData header, ClaimData claim)`

```csharp
// Check Insurance Type (priority)
if (claim != null && (claim.ClaimType == "07" || 
                      claim.ClaimType == "MB" || 
                      claim.ClaimType == "MC")) 
    return true;

// Check Payer Name
if (header?.PayerName.ToUpperInvariant().Contains("MEDICARE") ||
    header?.PayerName.ToUpperInvariant().Contains("CMS"))
    return true;

return false;
```

**Medicare Determination Affects:**
- Candidate selection priority
- 2% base calculation method
- Whether to use CMS Payment Base (Allowed - PR)

---

## **Data Flow Diagram**

```
Excel Input
    ↓
DataNormalization
    ↓
PatientResponsibilityFixer
    ↓
MathBalancingRule.PreProcessClaimSequestration()
    ├─ Detect Identical Amounts
    ├─ Confirm Claim-Level Status
    ├─ Calculate 5 Candidates per Line
    ├─ Distribute: 3-method per line + residue
    └─ Apply Distributed Amounts
    ↓
SequestrationService.ProcessClaim()
    ├─ Re-detect Level (validation)
    ├─ Confirm Service-Line Status (guard)
    ├─ Resolve Claim-Level Distribution
    └─ Sanitize Final Amounts
    ↓
SequestrationDetectionService.ProcessSequestration()
    ├─ Group by Amount
    ├─ Verify Each Line Mathematically
    └─ Deduplicate Claims vs Service-Line
    ↓
EDI Generation (Edi835Generator)
    ├─ Write Claim Line Adjustments
    ├─ Include CO-253 with resolved amounts
    └─ Output EDI 835 File
    ↓
Output
```

---

## **Key Configuration Parameters**

| Parameter | Value | Purpose |
|-----------|-------|---------|
| Tolerance | $0.001 | Identical amount detection |
| Exact Match | $0.01 | Service-line validation tolerance |
| Slack | $0.05 | Non-Medicare reverse engineering match |
| Sequestration Rate | 2% | Federal reduction percentage |
| Reverse Factor | 0.98 | Math inverse (paid = original * 0.98) |

---

## **Example Scenarios**

### **Scenario 1: Classic Claim-Level Sequestration (Medicare)**
```
Claim Claims Paid: $3,000 total
Lines:
  Line 1: Paid = $1,000, Allowed = $1,100
  Line 2: Paid = $1,000, Allowed = $1,100
  Line 3: Paid = $1,000, Allowed = $1,100

Excel Data: Each line has SequestrationAmount = $20

Processing:
  1. Detect: All amounts identical ($20) → CLAIM-LEVEL
  2. Confirm: No line's sequestration validates independently → CLAIM-LEVEL CONFIRMED
  3. Calculate 5-method candidates per line:
     - Method A: $1,000 / 0.98 * 0.02 = $20.41
     - Method B: ($1,100 - $0) * 0.02 = $22
     - Method C: $1,000 * 0.02 = $20
     - Method D: $1,100 * 0.02 = $22
     - Method E: $1,100 - $1,000 = $100
     Best for each line: $20 (Method C, closest to 2% target)
  4. Distribution:
     - Total to distribute: $20
     - Per line: $20 / 3 = $6.67
     - Line 1: $6.67
     - Line 2: $6.67
     - Line 3: $6.66
  5. Final: Each line gets CO-253 with calculated share
```

### **Scenario 2: Service-Line Level (Hidden in Identical Amounts)**
```
Claim Claims Paid: $2,500 total
Lines:
  Line 1: Paid = $500, Allowed = $500, Excel Seq = $10
  Line 2: Paid = $1,500, Allowed = $1,500, Excel Seq = $10
  Line 3: Paid = $500, Allowed = $500, Excel Seq = $10

Processing:
  1. Detect: All amounts identical ($10) → appears CLAIM-LEVEL
  2. Confirm: Check Line 2
     - Candidates: $500/0.98*0.02=$10.20, $1,500*0.02=$30, etc.
     - Found: Line 2's Excel seq ($10) matches Method C: $30? NO
     - Line 2's Excel seq ($10) doesn't match ANY service-line method
     - But Line 1: $500 * 0.02 = $10 ✓ MATCHES!
     → CLAIM-LEVEL DENIED (at least one line validates itself)
  3. Fallback: Handle as service-line level
     - Use per-line calculations
     - Line 1: Seq = $10 (justified by its own math)
     - Line 2: Seq = $10 (? may be recalculated)
     - Line 3: Seq = $10 (justified by its own math)
```

### **Scenario 3: Non-Medicare Payer with Gap Logic**
```
Claim Claims Paid: $2,000 total
Lines:
  Line 1: Charge = $1,000, Allowed = $950, Paid = $900
          Excel Seq = $50
  Line 2: Charge = $1,000, Allowed = $950, Paid = $900
          Excel Seq = $50

Processing:
  1. Detect: Identical ($50) → CLAIM-LEVEL
  2. Confirm: No line justifies $50 with 2% methods
  3. Resolve with 5 methods:
     - Method A: $900 / 0.98 * 0.02 = $18.37
     - Method C: $900 * 0.02 = $18
     - Method D: $950 * 0.02 = $19
     - Method E (Gap): $1,000 - $900 = $100 ← HIGH!
     Best for non-Medicare: Gap Logic = $100? But that's the full gap!
     → Recalc: ($1,000 - $950) = $50 ✓ (the charge-to-allowed gap)
     This matches Excel!
  4. Distribution: $50 per line (as gap)
     - Indicates charge exceeds allowed, not exactly 2% sequestration
```

---

## **Error Handling & Edge Cases**

### **Edge Case 1: No Co-253 Code Present**
```csharp
if (excelSeq == 0 && HasSequestrationCode(line))
{
    // Recalculate using best candidate
}
```
- Excel column has amount, but code is missing → Applies calculated code

### **Edge Case 2: Conflicting Amounts on Single Line**
```csharp
// Multiple adjustments with different sequestration values
// Sanitize: Keep first, zero others
```

### **Edge Case 3: Residue After Distribution**
```csharp
decimal totalRem = totalClaimSeq - runningSum;
if (Math.Abs(totalRem) > 0)
{
    results[lastKey] += totalRem;  // Give remainder to last line
}
```

### **Edge Case 4: Empty or Zero-Paid Lines**
```csharp
var nonZeroPaidLines = allLines.Where(x => x.LinePaidAmount > 0).ToList();
// Exclude lines with $0 paid to avoid division by zero
```

---

## **Summary Table: 3-Phase Processing**

| Phase | Class | Method | Purpose | Output |
|-------|-------|--------|---------|--------|
| **1** | MathBalancingRule | PreProcessClaimSequestration | Initial detection & distribution | Distributed amounts per line |
| **2** | SequestrationService | ProcessClaim | Validation & resolution | Confirmed amounts with CO-253 codes |
| **3** | SequestrationDetectionService | ProcessSequestration | Deduplication & verification | Final verified sequestration records |

---

## **Files Involved**

1. **Pipeline:** `Edi835Pipeline.cs` (lines 120-135)
2. **Pre-processing:** `MathBalancingRule.cs` (lines 63-150)
3. **Main Service:** `SequestrationService.cs` (complete file)
4. **Detection:** `SequestrationDetectionService.cs` (complete file)
5. **Interfaces:** `ISequestrationService.cs`, `ISequestrationDetectionService.cs`
6. **Helper:** `PatientResponsibilityFixerService.cs`

---

## **Conclusion**

The system uses a **3-phase adaptive approach**:
1. **Fast Path:** MathBalancingRule handles typical claim-level scenarios
2. **Validation Path:** SequestrationService confirms and re-calculates with 5-method strategy
3. **Deduplication Path:** SequestrationDetectionService ensures no double-counting

This design ensures claim-level sequestration (federal payment reductions of ~2%) is properly detected, distributed per line, and output to the EDI 835 with correct CO-253 adjustment codes.
