using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Edi.Generator835;
using Edi.Generator835.Services;
using Avalonia.Platform.Storage;
using Edi.Generator835.Configuration;
using Serilog;

using System.Collections.Generic;
using Edi.Generator835.Pipeline;

namespace EOB_TO_EDI_835.AvaloniaUI.ViewModels;

public partial class GeneratorViewModel : ViewModelBase
{
    public event Action<List<PipelineResult>>? ProcessingCompleted;

    [ObservableProperty]
    private string _title = "Configuration & Generation";

    [ObservableProperty]
    private string _inputPath = string.Empty;

    [ObservableProperty]
    private string _outputPath = string.Empty;

    [ObservableProperty]
    private string _configPath = string.Empty;

    [ObservableProperty]
    private string _templatePath = string.Empty;

    [ObservableProperty]
    private bool _enableParallelProcessing = true;

    [ObservableProperty]
    private bool _enableAppsmith = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private double _progressValue;

    public GeneratorViewModel()
    {
        // Try to load some defaults or search for them
        SearchForDefaultConfigs();
    }

    private void SearchForDefaultConfigs()
    {
        // Placeholder for logic to auto-locate common config files in the project structure
    }

    [RelayCommand]
    private async Task RunGeneratorAsync()
    {
        if (string.IsNullOrWhiteSpace(InputPath) || string.IsNullOrWhiteSpace(OutputPath) ||
            string.IsNullOrWhiteSpace(ConfigPath) || string.IsNullOrWhiteSpace(TemplatePath))
        {
            StatusMessage = "Error: All paths are required.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Initializing generator...";
        ProgressValue = 10;

        try
        {
            // Set up logging to a neutral location or the output folder
            LoggingProvider.Initialize(Path.Combine(OutputPath, "logs"));

            var results = await Task.Run(async () =>
            {
                var generator = new EnterpriseGenerator(ConfigPath, TemplatePath, OutputPath);
                return await generator.RunAsync(InputPath, EnableParallelProcessing, EnableAppsmith);
            });

            ProcessingCompleted?.Invoke(results);

            StatusMessage = "Processing completed successfully!";
            ProgressValue = 100;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Critical Error: {ex.Message}";
            Serilog.Log.Error(ex, "Generation failed");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
