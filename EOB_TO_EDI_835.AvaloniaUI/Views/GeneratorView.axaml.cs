using Avalonia.Controls;
using Avalonia.Input;
using System.Linq;
using EOB_TO_EDI_835.AvaloniaUI.ViewModels;
using Material.Styles.Assists;
using Avalonia.Interactivity;

namespace EOB_TO_EDI_835.AvaloniaUI.Views;

public partial class GeneratorView : UserControl
{
    public GeneratorView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (e.Source is Border border && border.Classes.Contains("dropzone"))
            {
                border.Classes.Add("dragging");
            }
        }
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        if (e.Source is Border border)
        {
            border.Classes.Remove("dragging");
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.Source is Border border)
        {
            border.Classes.Remove("dragging");
        }

        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            if (files != null && files.Any())
            {
                var path = files.First().Path.LocalPath;
                if (DataContext is GeneratorViewModel vm && e.Source is Border dropZone)
                {
                    if (dropZone.Name == "InputDropZone") vm.InputPath = path;
                    else if (dropZone.Name == "OutputDropZone") vm.OutputPath = path;
                    else if (dropZone.Name == "ConfigDropZone") vm.ConfigPath = path;
                    else if (dropZone.Name == "TemplateDropZone") vm.TemplatePath = path;
                }
            }
        }
    }

    private void OnInputClicked(object? sender, PointerPressedEventArgs e) => GetMainViewModel()?.PickInputPathCommand.Execute(null);
    private void OnOutputClicked(object? sender, PointerPressedEventArgs e) => GetMainViewModel()?.PickOutputPathCommand.Execute(null);
    private void OnConfigClicked(object? sender, PointerPressedEventArgs e) => GetMainViewModel()?.PickConfigPathCommand.Execute(null);
    private void OnTemplateClicked(object? sender, PointerPressedEventArgs e) => GetMainViewModel()?.PickTemplatePathCommand.Execute(null);

    private MainWindowViewModel? GetMainViewModel() => (this.VisualRoot as Window)?.DataContext as MainWindowViewModel;
}
