using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EOB_TO_EDI_835.AvaloniaUI.Models;

public partial class EdiSegment : ObservableObject
{
    [ObservableProperty]
    private string _tag = string.Empty;

    [ObservableProperty]
    private List<EdiElement> _elements = new();

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private int _absolutePosition;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _rawContent = string.Empty;

    [ObservableProperty]
    private bool _isHighlighted;
}

public partial class EdiElement : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isHighlighted;
}
