using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace SortFiles;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileTypeItem> _items = new();
    private readonly Dictionary<string, string> _friendlyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "Фото JPEG",
        [".jpeg"] = "Фото JPEG",
        [".png"] = "Изображение PNG",
        [".gif"] = "Изображение GIF",
        [".bmp"] = "Изображение BMP",
        [".heic"] = "Фото HEIC",
        [".tif"] = "Изображение TIFF",
        [".tiff"] = "Изображение TIFF",
        [".mp3"] = "Аудио MP3",
        [".flac"] = "Аудио FLAC",
        [".wav"] = "Аудио WAV",
        [".aac"] = "Аудио AAC",
        [".ogg"] = "Аудио OGG",
        [".wma"] = "Аудио WMA",
        [".mp4"] = "Видео MP4",
        [".mov"] = "Видео MOV",
        [".avi"] = "Видео AVI",
        [".mkv"] = "Видео MKV",
        [".doc"] = "Документ Word",
        [".docx"] = "Документ Word",
        [".xls"] = "Таблица Excel",
        [".xlsx"] = "Таблица Excel",
        [".xlsm"] = "Таблица Excel",
        [".ppt"] = "Презентация PowerPoint",
        [".pptx"] = "Презентация PowerPoint",
        [".pdf"] = "PDF документ",
        [".txt"] = "Текстовый файл",
        [".rtf"] = "Текстовый файл",
        [".csv"] = "CSV файл",
        [".zip"] = "Архив ZIP",
        [".rar"] = "Архив RAR",
        [".7z"] = "Архив 7z"
    };

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public MainWindow()
    {
        InitializeComponent();
        FileTypesGrid.DataContext = _items;
        App.ThemeChanged += OnThemeChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        UpdateTitleBarTheme();
    }

    protected override void OnClosed(EventArgs e)
    {
        App.ThemeChanged -= OnThemeChanged;
        base.OnClosed(e);
    }

    private void OnThemeChanged()
    {
        Dispatcher.Invoke(UpdateTitleBarTheme);
    }

    private void UpdateTitleBarTheme()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = App.IsLightTheme ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
        }
        catch
        {
            // ignore if not supported
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private void OnBrowseSource(object sender, RoutedEventArgs e)
    {
        var path = PickFolder();
        if (!string.IsNullOrWhiteSpace(path))
        {
            SourcePathBox.Text = path;
        }
    }

    private void OnBrowseDestination(object sender, RoutedEventArgs e)
    {
        var path = PickFolder();
        if (!string.IsNullOrWhiteSpace(path))
        {
            DestinationPathBox.Text = path;
        }
    }

    private string? PickFolder()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.AutoUpgradeEnabled = true;
        dialog.ShowNewFolderButton = true;
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        var source = SourcePathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            System.Windows.MessageBox.Show("Укажите существующую исходную папку.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _items.Clear();
        StatusText.Text = "Сканирование...";

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            await Task.Run(() =>
            {
                foreach (var file in EnumerateFilesSafe(source))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrWhiteSpace(ext)) continue;
                    lock (counts)
                    {
                        counts.TryGetValue(ext, out var c);
                        counts[ext] = c + 1;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка при сканировании: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Ошибка";
            return;
        }

        foreach (var pair in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key))
        {
            _items.Add(new FileTypeItem
            {
                Extension = pair.Key.ToLowerInvariant(),
                DisplayName = GetDisplayName(pair.Key),
                Count = pair.Value,
                IsSelected = false
            });
        }

        StatusText.Text = _items.Count == 0 ? "Файлы не найдены" : $"Найдено типов: {_items.Count}";
    }

    private async void OnCopySelected(object sender, RoutedEventArgs e)
    {
        var source = SourcePathBox.Text?.Trim();
        var destination = DestinationPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            System.Windows.MessageBox.Show("Укажите существующую исходную папку.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(destination))
        {
            System.Windows.MessageBox.Show("Укажите папку назначения.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selected = _items.Where(i => i.IsSelected).Select(i => i.Extension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selected.Count == 0)
        {
            System.Windows.MessageBox.Show("Выберите хотя бы один тип файла.", "Внимание",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(destination);
        StatusText.Text = "Копирование...";

        var copied = 0;
        var errors = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in EnumerateFilesSafe(source))
                {
                    var ext = Path.GetExtension(file);
                    if (!selected.Contains(ext)) continue;

                    try
                    {
                        var targetPath = GetUniqueTargetPath(destination, Path.GetFileName(file));
                        File.Copy(file, targetPath);
                        Interlocked.Increment(ref copied);
                    }
                    catch (Exception ex)
                    {
                        lock (errors)
                        {
                            errors.Add($"{file}: {ex.Message}");
                        }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Ошибка при копировании: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Ошибка";
            return;
        }

        if (errors.Count > 0)
        {
            System.Windows.MessageBox.Show("Некоторые файлы не удалось скопировать. Проверьте доступ и повторите.",
                "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        StatusText.Text = $"Скопировано файлов: {copied}";
    }

    private string GetDisplayName(string ext)
    {
        return _friendlyNames.TryGetValue(ext, out var name)
            ? $"{name} ({ext.ToLowerInvariant()})"
            : $"Файл ({ext.ToLowerInvariant()})";
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> dirs = Array.Empty<string>();
            try
            {
                files = Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly);
                dirs = Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
            }
            catch
            {
                // пропускаем недоступные папки
            }

            foreach (var file in files)
                yield return file;

            foreach (var sub in dirs)
                stack.Push(sub);
        }
    }

    private static string GetUniqueTargetPath(string destination, string fileName)
    {
        var target = Path.Combine(destination, fileName);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var index = 1;
        while (File.Exists(target))
        {
            target = Path.Combine(destination, $"{name}({index}){ext}");
            index++;
        }
        return target;
    }
}

public class FileTypeItem
{
    public bool IsSelected { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public int Count { get; set; }
}
