using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Material.Icons;

namespace EOB_TO_EDI_835.AvaloniaUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    [ObservableProperty]
    private NavigationItem _selectedNavigationItem;

    public ObservableCollection<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private bool _isDrawerOpen;

    public IStorageProvider? StorageProvider { get; set; }

    public MainWindowViewModel()
    {
        var gvm = new GeneratorViewModel();
        var rvm = new ResultsViewModel();
        var evm = new EdiEditorViewModel();
        var vvm = new ValidatorViewModel();

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new NavigationItem("Generator", MaterialIconKind.PlayCircleOutline, gvm),
            new NavigationItem("Validator", MaterialIconKind.ShieldCheckOutline, vvm),
            new NavigationItem("Results", MaterialIconKind.FormatListBulleted, rvm),
            new NavigationItem("Editor", MaterialIconKind.FileEditOutline, evm)
        };

        gvm.ProcessingCompleted += (results) =>
        {
            rvm.Results.Clear();
            foreach (var r in results) rvm.Results.Add(r);

            // Auto-navigate to results
            SelectedNavigationItem = NavigationItems.FirstOrDefault(x => x.Name == "Results")!;
        };

        rvm.OpenEditorRequested += async (filePath) =>
        {
            await evm.LoadFileAsync(filePath);
            SelectedNavigationItem = NavigationItems.FirstOrDefault(x => x.Name == "Editor")!;
        };

        vvm.OpenEditorRequested += async (filePath) =>
        {
            await evm.LoadFileAsync(filePath);
            SelectedNavigationItem = NavigationItems.FirstOrDefault(x => x.Name == "Editor")!;
        };

        _selectedNavigationItem = NavigationItems.First();
        _currentViewModel = _selectedNavigationItem.ViewModel;
    }

    [RelayCommand]
    private void ToggleDrawer() => IsDrawerOpen = !IsDrawerOpen;

    [RelayCommand]
    private async Task PickInputPathAsync()
    {
        if (StorageProvider == null) return;
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Input EOB Folder" });
        if (result.Count > 0 && CurrentViewModel is GeneratorViewModel gvm)
        {
            gvm.InputPath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task PickOutputPathAsync()
    {
        if (StorageProvider == null) return;
        var result = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Select Output EDI Directory" });
        if (result.Count > 0 && CurrentViewModel is GeneratorViewModel gvm)
        {
            gvm.OutputPath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task PickConfigPathAsync()
    {
        if (StorageProvider == null) return;
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Mapping Configuration",
            FileTypeFilter = new[] { new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } } }
        });
        if (result.Count > 0 && CurrentViewModel is GeneratorViewModel gvm)
        {
            gvm.ConfigPath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task PickTemplatePathAsync()
    {
        if (StorageProvider == null) return;
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select EOB Template",
            FileTypeFilter = new[] { new FilePickerFileType("Excel Files") { Patterns = new[] { "*.xlsx" } } }
        });
        if (result.Count > 0 && CurrentViewModel is GeneratorViewModel gvm)
        {
            gvm.TemplatePath = result[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task PickValidatorFilePathAsync()
    {
        if (StorageProvider == null) return;
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select EDI file for Validation",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("EDI Files") { Patterns = new[] { "*.edi", "*.txt", "*.835" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });
        if (result.Count > 0 && CurrentViewModel is ValidatorViewModel vvm)
        {
            vvm.SelectedFilePath = result[0].Path.LocalPath;
        }
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem value)
    {
        if (value != null)
        {
            CurrentViewModel = value.ViewModel;
        }
    }
}

public class NavigationItem
{
    public string Name { get; }
    public MaterialIconKind IconKind { get; }
    public ViewModelBase ViewModel { get; }

    public NavigationItem(string name, MaterialIconKind iconKind, ViewModelBase viewModel)
    {
        Name = name;
        IconKind = iconKind;
        ViewModel = viewModel;
    }
}
