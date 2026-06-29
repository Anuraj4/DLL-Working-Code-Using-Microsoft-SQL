using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Edi.Generator835.Services;
using Avalonia.Platform.Storage;
using Xalta.Edi.BalancingValidation.Core;

namespace EOB_TO_EDI_835.AvaloniaUI.ViewModels;

public partial class ValidatorViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _errors = new();

    [ObservableProperty]
    private int _selectedSnipLevel = 4; // Default to Level 4 (All)

    private readonly EdiValidationService _validationService = new();

    public event Action<string>? OpenEditorRequested;

    public ValidatorViewModel() { }

    [RelayCommand]
    private void BrowseFile()
    {
        // This will be handled by MainWindowViewModel setting the StorageProvider
        // or we can invoke an event. For simplicity in Avalonia with MVVM, 
        // we often pass the StorageProvider or use a service.
        // I'll add a command that MainWindowViewModel can hook into or use a shared service.
    }

    [RelayCommand]
    private async Task RunValidationAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
        {
            StatusMessage = "Please select a valid file first.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Validating...";
        Errors.Clear();

        try
        {
            await Task.Run(() =>
            {
                var validationErrors = _validationService.ValidateEdiFile(SelectedFilePath, SelectedSnipLevel);

                // Note: EdiValidationService.ValidateEdiFile currently runs SNIP 1-4 regardless of level.
                // We might want to filter or update EdiValidationService.

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var err in validationErrors.FlattenedErrors)
                    {
                        Errors.Add($"[{err.SegmentName} @ {err.SegmentPosition}] {err.ElementReference}: {err.Message}");
                    }

                    if (Errors.Count == 0)
                    {
                        StatusMessage = "Validation successful! No errors found.";
                    }
                    else
                    {
                        StatusMessage = $"Validation completed with {Errors.Count} error(s).";
                    }
                });
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Validation failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenInEditor()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath)) return;
        OpenEditorRequested?.Invoke(SelectedFilePath);
    }
}
