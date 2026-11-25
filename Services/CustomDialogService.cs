// CustomDialogService.cs — In-app custom popup dialogs with rounded corners and theme styling
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Burudani.Services;

public class DialogOptionItem
{
    public string DisplayText { get; set; } = string.Empty;
    public object? Value { get; set; }
    public bool IsSelected { get; set; }
}

public static class CustomDialogService
{
    public static event Action? StateChanged;

    public static bool IsVisible { get; private set; }
    public static string Title { get; private set; } = string.Empty;
    public static string Message { get; private set; } = string.Empty;
    public static string InputText { get; set; } = string.Empty;
    public static string ConfirmButtonText { get; private set; } = "OK";
    public static string CancelButtonText { get; private set; } = "Cancel";
    public static bool IsPrompt { get; private set; }
    public static bool IsOptions { get; private set; }
    public static bool IsConfirm { get; private set; }
    public static bool IsAlert { get; private set; }
    public static ObservableCollection<DialogOptionItem> Options { get; } = [];

    private static TaskCompletionSource<object?>? _tcs;

    public static Task<T?> ShowOptionsAsync<T>(string title, IEnumerable<T> items, Func<T, string> displayFunc, T? selected = default)
    {
        _tcs?.TrySetCanceled();
        _tcs = new TaskCompletionSource<object?>();

        Title = title;
        Message = string.Empty;
        IsOptions = true;
        IsPrompt = false;
        IsConfirm = false;
        IsAlert = false;
        CancelButtonText = "Cancel";

        Options.Clear();
        foreach (var item in items)
        {
            bool isSel = selected != null && EqualityComparer<T>.Default.Equals(item, selected);
            Options.Add(new DialogOptionItem
            {
                DisplayText = displayFunc(item),
                Value = item,
                IsSelected = isSel
            });
        }

        IsVisible = true;
        StateChanged?.Invoke();

        return _tcs.Task.ContinueWith(t => t.IsCompletedSuccessfully && t.Result is T val ? val : default);
    }

    public static Task<string?> ShowPromptAsync(string title, string message, string initialValue = "", string confirmText = "OK", string cancelText = "Cancel")
    {
        _tcs?.TrySetCanceled();
        _tcs = new TaskCompletionSource<object?>();

        Title = title;
        Message = message;
        InputText = initialValue;
        ConfirmButtonText = confirmText;
        CancelButtonText = cancelText;
        IsPrompt = true;
        IsOptions = false;
        IsConfirm = false;
        IsAlert = false;

        IsVisible = true;
        StateChanged?.Invoke();

        return _tcs.Task.ContinueWith(t => t.IsCompletedSuccessfully ? t.Result as string : null);
    }

    public static Task<bool> ShowConfirmAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel")
    {
        _tcs?.TrySetCanceled();
        _tcs = new TaskCompletionSource<object?>();

        Title = title;
        Message = message;
        ConfirmButtonText = confirmText;
        CancelButtonText = cancelText;
        IsConfirm = true;
        IsPrompt = false;
        IsOptions = false;
        IsAlert = false;

        IsVisible = true;
        StateChanged?.Invoke();

        return _tcs.Task.ContinueWith(t => t.IsCompletedSuccessfully && t.Result is true);
    }

    public static Task ShowAlertAsync(string title, string message, string buttonText = "OK")
    {
        _tcs?.TrySetCanceled();
        _tcs = new TaskCompletionSource<object?>();

        Title = title;
        Message = message;
        ConfirmButtonText = buttonText;
        CancelButtonText = string.Empty;
        IsAlert = true;
        IsConfirm = false;
        IsPrompt = false;
        IsOptions = false;

        IsVisible = true;
        StateChanged?.Invoke();

        return _tcs.Task;
    }

    public static void SelectOption(DialogOptionItem item)
    {
        IsVisible = false;
        StateChanged?.Invoke();
        _tcs?.TrySetResult(item.Value);
    }

    public static void Confirm()
    {
        IsVisible = false;
        StateChanged?.Invoke();
        if (IsPrompt)
            _tcs?.TrySetResult(InputText);
        else if (IsConfirm)
            _tcs?.TrySetResult(true);
        else
            _tcs?.TrySetResult(null);
    }

    public static void Cancel()
    {
        IsVisible = false;
        StateChanged?.Invoke();
        if (IsConfirm)
            _tcs?.TrySetResult(false);
        else
            _tcs?.TrySetResult(null);
    }
}
