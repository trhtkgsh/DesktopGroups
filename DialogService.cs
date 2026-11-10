using System.Windows;
using Microsoft.Win32;

namespace DesktopGroups;

public interface IDialogService
{
    bool Confirm(string message, string title);
    void ShowInfo(string message, string title);
    void ShowWarning(string message, string title);
    void ShowError(string message, string title);
    string[]? PickFiles();
    string[]? PickFolders();
    string? PickIconFile();
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public bool Confirm(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowInfo(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title) =>
        MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public string[]? PickFiles()
    {
        var dialog = new OpenFileDialog { Title = "בחירת קובץ או תוכנה", Filter = "כל הקבצים|*.*", Multiselect = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : null;
    }

    public string[]? PickFolders()
    {
        var dialog = new OpenFolderDialog { Title = "בחירת תיקייה", Multiselect = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FolderNames : null;
    }

    public string? PickIconFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "בחירת סמל",
            Filter = "קובצי סמל, תמונה או תוכנה|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|כל הקבצים|*.*"
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }
}
