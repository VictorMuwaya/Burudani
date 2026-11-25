// SearchPage.xaml.cs — Dedicated Search View with Auto-Hide Keyboard
using Burudani.Helpers;
using Burudani.Services;
using Burudani.ViewModels;

namespace Burudani.Views;

public partial class SearchPage : ContentView
{
    public SearchPage()
    {
        InitializeComponent();
    }

    private void OnSearchCompleted(object? sender, EventArgs e)
    {
        SearchEntry.Unfocus();
        KeyboardHelper.HideKeyboard();
        ViewModel.HasSuggestions = false;
        ViewModel.Suggestions.Clear();
        ViewModel.SearchCommand.Execute(null);
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        ViewModel.UpdateAutocompleteCommand.Execute(e.NewTextValue);
    }

    private void OnSuggestionTapped(object? sender, TappedEventArgs e)
    {
        SearchEntry.Unfocus();
        KeyboardHelper.HideKeyboard();
        if (sender is Border border && border.BindingContext is string text)
        {
            ViewModel.HasSuggestions = false;
            ViewModel.Suggestions.Clear();
            ViewModel.SelectSuggestionCommand.Execute(text);
        }
    }
}
