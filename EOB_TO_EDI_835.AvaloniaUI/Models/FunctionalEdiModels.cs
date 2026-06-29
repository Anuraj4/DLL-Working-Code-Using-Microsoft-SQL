using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EOB_TO_EDI_835.AvaloniaUI.Models;

/// <summary>Header info from BPR, TRN, and header-level N1/N3/N4/PER/REF</summary>
public partial class TransactionHeaderInfo : ObservableObject
{
    // BPR
    [ObservableProperty] private string _paymentMethod = string.Empty; // BPR01
    [ObservableProperty] private decimal _paymentAmount;               // BPR02
    [ObservableProperty] private string _paymentDate = string.Empty;   // BPR16

    // TRN
    [ObservableProperty] private string _traceNumber = string.Empty;   // TRN02
    [ObservableProperty] private string _traceOriginatingId = string.Empty; // TRN03

    // Header-level REF
    public ObservableCollection<GenericEdiData> HeaderReferences { get; } = new();

    // Payer & Payee entities
    public EntityInfo Payer { get; set; } = new();
    public EntityInfo Payee { get; set; } = new();
}

public partial class EntityInfo : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;     // N1*02
    [ObservableProperty] private string _idQualifier = string.Empty; // N1*03
    [ObservableProperty] private string _id = string.Empty;       // N1*04
    [ObservableProperty] private string _address1 = string.Empty; // N3*01
    [ObservableProperty] private string _address2 = string.Empty; // N3*02
    [ObservableProperty] private string _city = string.Empty;     // N4*01
    [ObservableProperty] private string _state = string.Empty;    // N4*02
    [ObservableProperty] private string _zip = string.Empty;      // N4*03
    [ObservableProperty] private string _contactName = string.Empty;  // PER*02
    [ObservableProperty] private string _contactPhone = string.Empty; // PER*04
    [ObservableProperty] private string _contactEmail = string.Empty; // PER*08
    public ObservableCollection<GenericEdiData> References { get; } = new(); // REF
}

public partial class GenericEdiData : ObservableObject
{
    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;
}

public partial class AdjustmentData : ObservableObject
{
    [ObservableProperty]
    private string _groupCode = string.Empty;

    [ObservableProperty]
    private string _reasonCode = string.Empty;

    [ObservableProperty]
    private decimal _amount;
}

public partial class ClaimViewModel : ObservableObject
{
    [ObservableProperty]
    private string _claimId = string.Empty; // CLP01

    [ObservableProperty]
    private string _status = string.Empty; // CLP02

    [ObservableProperty]
    private decimal _totalCharge; // CLP03

    [ObservableProperty]
    private decimal _totalPaid; // CLP04

    [ObservableProperty]
    private string _patientControlNumber = string.Empty; // CLP07

    // Additional extracted data
    [ObservableProperty]
    private string _patientName = string.Empty; // NM1*QC

    [ObservableProperty]
    private string _providerName = string.Empty; // NM1*82/PE

    [ObservableProperty]
    private string _payerName = string.Empty; // NM1*PR

    public ObservableCollection<GenericEdiData> ClaimReferences { get; } = new(); // REF
    public ObservableCollection<AdjustmentData> ClaimAdjustments { get; } = new(); // CAS
    public ObservableCollection<GenericEdiData> ClaimAmounts { get; } = new(); // AMT

    public ObservableCollection<ServiceLineViewModel> ServiceLines { get; } = new();

    public EdiSegment? OriginalSegment { get; set; }
}

public partial class ServiceLineViewModel : ObservableObject
{
    [ObservableProperty]
    private string _serviceCode = string.Empty; // SVC01-2

    [ObservableProperty]
    private decimal _charge; // SVC02

    [ObservableProperty]
    private decimal _paid; // SVC03

    [ObservableProperty]
    private string _revenueCode = string.Empty; // SVC04

    [ObservableProperty]
    private string _serviceDate = string.Empty; // DTM (472)

    public ObservableCollection<GenericEdiData> ServiceReferences { get; } = new(); // REF
    public ObservableCollection<AdjustmentData> ServiceAdjustments { get; } = new(); // CAS
    public ObservableCollection<GenericEdiData> ServiceAmounts { get; } = new(); // AMT

    public EdiSegment? OriginalSegment { get; set; }
}
