using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EOB_TO_EDI_835.AvaloniaUI.Models;
using Edi.Generator835.Services;
using Edi.Generator835.Pipeline;
using Xalta.Edi.BalancingValidation.Core;

namespace EOB_TO_EDI_835.AvaloniaUI.ViewModels;

public partial class EdiEditorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private ObservableCollection<EdiSegment> _segments = new();

    [ObservableProperty]
    private ObservableCollection<ClaimViewModel> _claims = new();

    [ObservableProperty]
    private string _senderName = "Unknown Sender"; // ISA06

    [ObservableProperty]
    private string _receiverName = "Unknown Receiver"; // ISA08

    [ObservableProperty]
    private EdiSegment? _selectedSegment;

    [ObservableProperty]
    private ObservableCollection<ValidationError> _validationErrors = new();

    [ObservableProperty]
    private ValidationError? _selectedError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private TransactionHeaderInfo? _transactionHeader;

    [ObservableProperty]
    private bool _isValid = true;

    private readonly EdiValidationService _validationService = new();
    private readonly Services.PdfExportService _pdfService = new();
    private readonly Services.EdiMappingService _mappingService = new();

    public EdiEditorViewModel() { }

    [RelayCommand]
    private void ExportToPdf()
    {
        if (string.IsNullOrEmpty(FilePath)) return;
        try
        {
            var savedPath = _pdfService.ExportClaimsToPdf(FilePath, SenderName, ReceiverName, TransactionHeader, Claims);
            StatusMessage = $"PDF saved to: {savedPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF export failed: {ex.Message}";
        }
    }

    public async Task LoadFileAsync(string path)
    {
        FilePath = path;
        IsBusy = true;

        try
        {
            var content = await File.ReadAllTextAsync(path);
            ParseEdi(content);
            RunValidation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ParseEdi(string content)
    {
        Segments.Clear();
        var segmentStrings = content.Split('~', StringSplitOptions.RemoveEmptyEntries);

        int dataSegmentCounter = -1;
        bool stFound = false;
        int absoluteCounter = 0;

        foreach (var segStr in segmentStrings)
        {
            var trimmedSeg = segStr.Trim();
            if (string.IsNullOrWhiteSpace(trimmedSeg)) continue;

            var parts = trimmedSeg.Split('*');
            if (parts.Length == 0) continue;

            var tag = parts[0].Trim();
            if (tag == "ST")
            {
                stFound = true;
                dataSegmentCounter = 0;
            }
            else if (stFound)
            {
                dataSegmentCounter++;
            }

            var segment = new EdiSegment
            {
                Tag = tag,
                Position = dataSegmentCounter, // Relative (ST=0, BPR=1)
                AbsolutePosition = absoluteCounter++, // 0-based from ISA
                RawContent = trimmedSeg,
                Elements = parts.Skip(1).Select((val, idx) => new EdiElement
                {
                    Index = idx + 1,
                    Value = val
                }).ToList()
            };

            Segments.Add(segment);
        }

        MapToFunctionalModels(content);
    }

    private void MapToFunctionalModels(string content)
    {
        var (sender, receiver, header, claims) = _mappingService.MapEdiToFunctionalModels(content);

        SenderName = sender;
        ReceiverName = receiver;
        TransactionHeader = header;

        Claims.Clear();
        foreach (var claim in claims) Claims.Add(claim);
    }

    private void RunValidation()
    {
        foreach (var seg in Segments)
        {
            seg.HasError = false;
            foreach (var el in seg.Elements) el.HasError = false;
        }
        ValidationErrors.Clear();

        var currentEdi = BuildEdiString();
        var internalResults = _validationService.ValidateEdiString(currentEdi);

        // 1. Process validation results and map to UI segments
        foreach (var segErr in internalResults.Errors)
        {
            var targetSegment = FindSegment(segErr.SegmentPosition, segErr.SegmentName);

            if (targetSegment != null) targetSegment.HasError = true;

            foreach (var elemErr in segErr.ElementErrors)
            {
                if (targetSegment != null && elemErr.ElementPosition >= 0)
                {
                    var targetElement = targetSegment.Elements.FirstOrDefault(e => e.Index == elemErr.ElementPosition);
                    if (targetElement != null) targetElement.HasError = true;
                }
            }
        }

        // 2. Populate the UI error list using the standardized flattened results
        foreach (var err in internalResults.FlattenedErrors)
        {
            ValidationErrors.Add(err);
        }

        IsValid = !ValidationErrors.Any();
    }

    partial void OnSelectedErrorChanged(ValidationError? value)
    {
        // 1. Reset all highlights first
        foreach (var seg in Segments)
        {
            seg.IsHighlighted = false;
            foreach (var el in seg.Elements) el.IsHighlighted = false;
        }

        if (value == null) return;

        int? segmentPosition = value.SegmentPosition;
        int? elementIndex = value.ElementPosition;

        // 2. Apply highlighting with precision (ST-relative Position Match)
        if (segmentPosition.HasValue)
        {
            var targetSegment = FindSegment(segmentPosition.Value, value.SegmentName);

            // Fallback for Position -1 (Balancing Errors from Rules)
            if (targetSegment == null && segmentPosition == -1)
            {
                if (value.SegmentName == "CLP")
                {
                    // For CLP, try to find by Patient Control Number if it's in the message
                    var claimMatch = Regex.Match(value.Message ?? "", @"Claim (.*?) balancing failed");
                    if (claimMatch.Success)
                    {
                        var claimId = claimMatch.Groups[1].Value;
                        targetSegment = Segments.FirstOrDefault(s => s.Tag == "CLP" && s.Elements.Any(e => e.Index == 1 && e.Value == claimId));
                    }
                }
                else if (value.SegmentName == "SVC")
                {
                    // For SVC, try to find by Procedure Code if it's in the message
                    var svcMatch = Regex.Match(value.Message ?? "", @"Service Line (.*?) balancing failed");
                    if (svcMatch.Success)
                    {
                        var procCode = svcMatch.Groups[1].Value;
                        targetSegment = Segments.FirstOrDefault(s => s.Tag == "SVC" && s.Elements.Any(e => e.Index == 1 && e.Value.Contains(procCode)));
                    }
                }
            }

            if (targetSegment != null)
            {
                targetSegment.IsHighlighted = true;
                SelectedSegment = targetSegment;

                if (elementIndex.HasValue && elementIndex > 0)
                {
                    var targetElement = targetSegment.Elements.FirstOrDefault(e => e.Index == elementIndex.Value);
                    if (targetElement != null)
                    {
                        targetElement.IsHighlighted = true;
                    }
                }
            }
        }
    }

    private EdiSegment? FindSegment(int segmentPosition, string segmentName)
    {
        if (segmentPosition < 0) return null;

        // 1. Exact match (adjusting 1-based to 0-based)
        var targetIndex = segmentPosition - 1;
        var seg = Segments.FirstOrDefault(s => s.Position == targetIndex && s.Tag == segmentName);
        if (seg != null) return seg;

        // 2. Fuzzy match (+1)
        seg = Segments.FirstOrDefault(s => s.Position == segmentPosition && s.Tag == segmentName);
        if (seg != null) return seg;

        // 3. Fuzzy match (-1)
        seg = Segments.FirstOrDefault(s => s.Position == (segmentPosition - 2) && s.Tag == segmentName);
        if (seg != null) return seg;

        return null;
    }

    private string BuildEdiString()
    {
        return string.Join("~", Segments.Select(s =>
            s.Tag + "*" + string.Join("*", s.Elements.Select(e => e.Value)))) + "~";
    }

    [RelayCommand]
    private async Task SaveChangesAsync()
    {
        if (string.IsNullOrEmpty(FilePath)) return;

        IsBusy = true;
        try
        {
            var content = BuildEdiString();
            await File.WriteAllTextAsync(FilePath, content);
            RunValidation();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ReValidate()
    {
        RunValidation();
    }
}
