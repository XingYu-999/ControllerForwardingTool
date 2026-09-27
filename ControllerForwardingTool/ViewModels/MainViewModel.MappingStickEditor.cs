using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ControllerForwardingTool.Input;

namespace ControllerForwardingTool.ViewModels;

public partial class MainViewModel
{
    private StickMappingEditSession? mappingStickEditor;
    [ObservableProperty] public partial int MappingEditorTab { get; set; }
    [ObservableProperty] public partial int MappingStickSourceIndex { get; set; }
    [ObservableProperty] public partial string MappingStickEditorHint { get; set; } = "";
    public bool HasStickMappingEditor => mappingStickEditor is not null;
    public string MappingButtonTabTitle => HasStickMappingEditor ? "摇杆按下" : "按键配置";
    public IReadOnlyList<string> MappingStickSources { get; } = ["源左摇杆", "源右摇杆"];
    public ObservableCollection<MappingStickDirectionRow> MappingStickDirections { get; } = [];
    public bool CanBindStickMouseLeft => mappingStickEditor?.Recording is not null;

    partial void OnMappingEditorTabChanged(int value)
    {
        mappingEditor?.RequireRelease(); mappingStickEditor?.RequireRelease();
        MappingEditorHint = "请先松开按键，再录入来源；点击确认后一起提交两个页面的配置。";
        RefreshStickMappingEditor();
    }
    partial void OnMappingStickSourceIndexChanged(int value)
    {
        if (mappingStickEditor is { KeyboardOnly: false } editor && value is 0 or 1)
            editor.Source = (StickSource)value;
    }
    private void RefreshStickMappingEditor()
    {
        OnPropertyChanged(nameof(HasStickMappingEditor)); OnPropertyChanged(nameof(MappingButtonTabTitle));
        OnPropertyChanged(nameof(CanBindStickMouseLeft));
        MappingStickDirections.Clear();
        if (mappingStickEditor is not { } editor) return;
        MappingStickSourceIndex = (int)editor.Source;
        MappingStickEditorHint = editor.Hint;
        foreach (var (direction, key) in editor.Keys)
            MappingStickDirections.Add(new(direction, KeyboardStickMapping.Name(direction),
                editor.Recording == direction ? "请按下源按键…" : key == 0 ? "未绑定 · 点击录入" : KeyboardMapping.KeyName(key)));
    }
    private void SyncStickEditorKeys()
    {
        if (mappingStickEditor is not { } editor) return;
        foreach (int key in editor.AssignedKeys) mappingEditor?.RemoveKey(key);
        RefreshMappingEditorSources(); RefreshStickMappingEditor();
    }
    [RelayCommand] private void RecordStickDirection(StickDirection direction)
    { mappingStickEditor?.BeginDirection(direction); RefreshStickMappingEditor(); }
    [RelayCommand] private void ClearStickDirection(StickDirection direction)
    { mappingStickEditor?.ClearDirection(direction); RefreshStickMappingEditor(); }
    [RelayCommand] private void BindStickMouseLeft()
    { mappingStickEditor?.BindMouseLeft(); SyncStickEditorKeys(); }
}

public sealed record MappingStickDirectionRow(StickDirection Direction, string Label, string KeyLabel);
