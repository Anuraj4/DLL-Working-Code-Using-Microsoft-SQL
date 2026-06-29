using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using EOB_TO_EDI_835.AvaloniaUI.Services;
using Edi.Generator835.Pipeline;
using System.Threading.Tasks;

namespace EOB_TO_EDI_835.AvaloniaUI.ViewModels;

public partial class ResultsViewModel : ViewModelBase
{
    private readonly EdiMappingService _mappingService = new();
    private readonly PdfExportService _pdfService = new();

    [ObservableProperty]
    private string _title = "Results & Validation Errors";

    [ObservableProperty]
    private ObservableCollection<PipelineResult> _results = new();

    [ObservableProperty]
    private PipelineResult? _selectedResult;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public event Action<string>? OpenEditorRequested;

    [RelayCommand]
    private void OpenEditor()
    {
        if (SelectedResult != null && !string.IsNullOrEmpty(SelectedResult.OutputFilePath))
        {
            OpenEditorRequested?.Invoke(SelectedResult.OutputFilePath);
        }
    }

    [RelayCommand]
    private async Task ExportToPdfAsync()
    {
        if (SelectedResult == null || string.IsNullOrEmpty(SelectedResult.OutputFilePath)) return;

        try
        {
            var content = await File.ReadAllTextAsync(SelectedResult.OutputFilePath);
            var (sender, receiver, header, claims) = _mappingService.MapEdiToFunctionalModels(content);
            var savedPath = _pdfService.ExportClaimsToPdf(SelectedResult.OutputFilePath, sender, receiver, header, claims);
            StatusMessage = $"PDF saved to: {savedPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"PDF export failed: {ex.Message}";
        }
    }
}
