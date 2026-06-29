using System;
using System.Collections.Generic;
using System.Linq;

namespace Edi.Generator835.Models
{
    /// <summary>
    /// Root data model representing all EOB data needed to generate an EDI 835 transaction.
    /// Populated by reading the Eob_Data.xlsx input file.
    /// </summary>
    public class Edi835DataModel
    {
        public HeaderData Header { get; set; } = new HeaderData();
        public List<ClaimData> Claims { get; set; } = new List<ClaimData>();
        public List<ProviderAdjustmentData> ProviderAdjustments { get; set; } = new List<ProviderAdjustmentData>();
    }

    /// <summary>
    /// payment_header sheet → ISA, GS, BPR, TRN, N1 segments.
    /// </summary>
    public class HeaderData
    {
        // Identifiers
        public string PaymentId { get; set; } = string.Empty;
        public string PayerName { get; set; } = string.Empty;
        public string PayerId { get; set; } = string.Empty;
        public string PayerAddressLine1 { get; set; } = string.Empty;
        public string PayerCity { get; set; } = string.Empty;
        public string PayerState { get; set; } = string.Empty;
        public string PayerZip { get; set; } = string.Empty;

        // Provider (Payee)
        public string ProviderName { get; set; } = string.Empty;
        public string ProviderNpi { get; set; } = string.Empty;
        public string ProviderTaxId { get; set; } = string.Empty;
        public string ProviderAddressLine1 { get; set; } = string.Empty;
        public string ProviderCity { get; set; } = string.Empty;
        public string ProviderState { get; set; } = string.Empty;
        public string ProviderZip { get; set; } = string.Empty;

        // Payment Info → BPR segment
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentDate { get; set; } = string.Empty;
        public string CheckOrEftNumber { get; set; } = string.Empty;
        public string RoutingNumber { get; set; } = string.Empty;
        public string BankAccountNumber { get; set; } = string.Empty;
        public decimal TotalPaymentAmount { get; set; }
        public string PayerEobType { get; set; } = string.Empty;
        public string CurrencySymbol { get; set; } = string.Empty;
        public string CurrencyCode { get; set; } = string.Empty;
        public string PayerCommunicationNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// claims sheet → CLP, NM1, DTM segments (Loop 2100).
    /// </summary>
    public class ClaimData
    {
        public string PaymentId { get; set; } = string.Empty;
        public string ClaimIdPayer { get; set; } = string.Empty;
        public string ClaimIdProvider { get; set; } = string.Empty;
        public string ClaimType { get; set; } = string.Empty;

        // Patient
        public string PatientName { get; set; } = string.Empty;
        public string PatientLastName { get; set; } = string.Empty;
        public string PatientFirstName { get; set; } = string.Empty;
        public string PatientDob { get; set; } = string.Empty;
        public string PatientId { get; set; } = string.Empty;

        // Subscriber
        public string SubscriberName { get; set; } = string.Empty;
        public string SubscriberId { get; set; } = string.Empty;

        // Provider
        public string ProviderRenderingName { get; set; } = string.Empty;
        public string ProviderRenderingNpi { get; set; } = string.Empty;

        // Claim amounts
        public string ClaimStatusCode { get; set; } = string.Empty;
        public decimal ClaimBilledAmount { get; set; }
        public decimal? ClaimAllowedAmount { get; set; }
        public decimal ClaimPaidAmount { get; set; }
        public decimal? PatientResponsibilityAmount { get; set; }

        /// <summary>
        /// True when the original ClaimPaidAmount was negative (reversal/takeback).
        /// Detected during normalization, used at EDI output to negate CLP/SVC/CAS amounts.
        /// </summary>
        public bool IsReversal { get; set; } = false;

        // Dates
        public string ClaimServiceDateFrom { get; set; } = string.Empty;
        public string ClaimServiceDateTo { get; set; } = string.Empty;

        // Remark codes
        public string ClaimRemarkCodes { get; set; } = string.Empty;

        // Child collections
        public List<ServiceLineData> ServiceLines { get; set; } = new List<ServiceLineData>();
        public List<AdjustmentData> ClaimAdjustments { get; set; } = new List<AdjustmentData>();
    }

    /// <summary>
    /// service_lines sheet → SVC, DTM segments (Loop 2110).
    /// </summary>
    public class ServiceLineData
    {
        public string ClaimIdPayer { get; set; } = string.Empty;
        public string ServiceLineId { get; set; } = string.Empty;
        public string CptCode { get; set; } = string.Empty;
        public string Modifier1 { get; set; } = string.Empty;
        public string Modifier2 { get; set; } = string.Empty;
        public string RevenueCode { get; set; } = string.Empty;
        public string NdcCode { get; set; } = string.Empty;
        public string Units { get; set; } = string.Empty;
        public string LineServiceDateFrom { get; set; } = string.Empty;
        public string LineServiceDateTo { get; set; } = string.Empty;
        public decimal LineBilledAmount { get; set; }
        public decimal? LineAllowedAmount { get; set; }
        public decimal LinePaidAmount { get; set; }


        public decimal? LinePatientResponsibilityAmount { get; set; }

        public string LineRemarkCodes { get; set; } = string.Empty;

        public string LineExplanationCodes { get; set; } = string.Empty;

        /// <summary>
        /// Property bag for any extra columns found in the Excel that are not explicitly mapped.
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ServiceLineData Clone()
        {
            var clone = (ServiceLineData)this.MemberwiseClone();
            clone.Metadata = new Dictionary<string, string>(this.Metadata, StringComparer.OrdinalIgnoreCase);
            
            // Adjustments need deep copying too
            clone.Adjustments = this.Adjustments.Select(a => a.Clone()).ToList();
            
            return clone;
        }

        // Adjustments at service-line level
        public List<AdjustmentData> Adjustments { get; set; } = new List<AdjustmentData>();
    }

    /// <summary>
    /// adjustments sheet → CAS segments (Loop 2100 or 2110).
    /// </summary>
    public class AdjustmentData
    {
        public string AdjustmentLevel { get; set; } = string.Empty;  // CLAIM or SERVICE_LINE
        public string PaymentId { get; set; } = string.Empty;
        public string ClaimIdPayer { get; set; } = string.Empty;
        public string ServiceLineId { get; set; } = string.Empty;    // Only for SERVICE_LINE level
        public string CptCode { get; set; } = string.Empty;          // Only for SERVICE_LINE level
        public string AdjustmentGroupCode { get; set; } = string.Empty;  // CO, PR, OA, PI
        public string AdjustmentReasonCode { get; set; } = string.Empty; // CARC code
        public decimal AdjustmentAmount { get; set; }

        public decimal? DeductibleAmount { get; set; }
        public decimal? CoinsuranceAmount { get; set; }
        public decimal? CopayAmount { get; set; }

        public decimal? OtherInsuranceAmount { get; set; }
        public decimal? SequestrationAmount { get; set; }
        public decimal? Quantity { get; set; }
        public string RemarkCode { get; set; } = string.Empty;      // RARC code
        public string Explanation { get; set; } = string.Empty;      // Descriptive text or codes

        /// <summary>
        /// Bucket of special CARC/CAGC codes found during normalization.
        /// Signals to the fixer which specific codes appeared in the raw data.
        /// Values: "PR1","PR2","PR3","CO253","OA23"
        /// </summary>
        public HashSet<string> SpecialCodeBucket { get; set; } = new HashSet<string>();

        /// <summary>
        /// Property bag for any extra columns found in the Excel that are not explicitly mapped.
        /// Keys are column headers, values are original strings.
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Performs a deep copy of the adjustment object, including the metadata bag.
        /// </summary>
        public AdjustmentData Clone()
        {
            var clone = (AdjustmentData)this.MemberwiseClone();
            
            // Deep copy the metadata dictionary
            clone.Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (this.Metadata != null)
            {
                foreach (var kvp in this.Metadata)
                {
                    clone.Metadata[kvp.Key] = kvp.Value;
                }
            }
            
            // Deep copy the special code set
            if (this.SpecialCodeBucket != null)
            {
                clone.SpecialCodeBucket = new HashSet<string>(this.SpecialCodeBucket, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                clone.SpecialCodeBucket = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            
            return clone;
        }
    }

    /// <summary>
    /// plb sheet → PLB segment.
    /// </summary>
    public class ProviderAdjustmentData
    {
        public string PaymentId { get; set; } = string.Empty;
        public string ProviderIdentifier { get; set; } = string.Empty;
        public string PlbReasonCode { get; set; } = string.Empty;
        public decimal PlbAmount { get; set; }
        public string FiscalPeriodDate { get; set; } = string.Empty;
    }
}
