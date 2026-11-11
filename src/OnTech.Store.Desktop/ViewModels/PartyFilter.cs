using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OnTech.Store.Desktop.ViewModels;

/// <summary>One customer or supplier in a log's party filter.</summary>
public sealed record PartyChoice(int Id, int? Number, string Name)
{
    public string Display => Number is { } n ? $"{n} - {Name}" : Name;
}

/// <summary>
/// The customer (issue vouchers, demo returns) or supplier (purchases) filter of a voucher log: type a number or part of
/// the name to narrow the list, pick one, and the log shows only that party's vouchers with their totals.
/// </summary>
public sealed partial class PartyFilter(Action changed) : ObservableObject
{
    private List<PartyChoice> _all = new();
    public ObservableCollection<PartyChoice> Choices { get; } = new();
    public bool IsLoaded { get; private set; }

    [ObservableProperty] private string? _search;
    [ObservableProperty] private PartyChoice? _selected;

    public void SetAll(IEnumerable<PartyChoice> all)
    {
        _all = all.ToList();
        IsLoaded = true;
        Filter();
    }

    partial void OnSearchChanged(string? value) => Filter();
    partial void OnSelectedChanged(PartyChoice? value) => changed();

    private void Filter()
    {
        var text = Search?.Trim() ?? "";
        var number = OnTech.Store.Client.Numbers.NormalizeDigits(text);
        var keep = Selected;
        var matches = text.Length == 0
            ? _all
            : _all.Where(p => p.Number?.ToString() == number || p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        Choices.Clear();
        foreach (var p in matches) Choices.Add(p);
        if (matches.Count == 1 && text.Length > 0) Selected = matches[0];
        else if (keep is not null) Selected = Choices.FirstOrDefault(p => p.Id == keep.Id);
    }

    [RelayCommand]
    private void Clear()
    {
        Search = null;
        Selected = null;
    }
}
