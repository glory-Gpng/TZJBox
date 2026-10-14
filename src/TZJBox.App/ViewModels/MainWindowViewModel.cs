using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TZJBox.App.Services;
using TZJBox.Sorting;

namespace TZJBox.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".avif", ".heic", ".heif"
    };

    private readonly ICapturedTimeReader _times;
    private readonly IUserDialogs _dialogs;
    private readonly List<FileSortInput> _cache = [];
    private List<FileRowViewModel> _rows = [];
    private List<RenamePlanItem> _plan = [];
    private CancellationTokenSource? _patternDebounce;
    private int _generation;
    private int _previewGeneration;
    private int _pendingRenames;
    private string _statusBase = "选择一个图片文件夹。琥珀色数字是这次真正参与排序的依据。";

    public MainWindowViewModel(ICapturedTimeReader times, IUserDialogs dialogs)
    {
        _times = times;
        _dialogs = dialogs;
        Strategies =
        [
            new StrategyChoice(SortStrategy.Auto, "智能识别", "看整个文件夹的补零、连续性和 page / 第X话 这类语义，自动挑真序号。"),
            new StrategyChoice(SortStrategy.LastNumber, "末尾序号优先", "去掉分辨率和日期后，用文件名里最后一个数字。专治 001_10。"),
            new StrategyChoice(SortStrategy.PrefixStrip, "前缀剥离", "跳过前几个分隔符，再对剩下的部分做自然排序。"),
            new StrategyChoice(SortStrategy.CustomRegex, "正则捕获", "自己写正则，用括号标出序号。例如 ^\\d+_(\\d+)。"),
            new StrategyChoice(SortStrategy.Metadata, "拍摄时间", "文件名彻底乱了时，优先 EXIF 拍摄时间，否则用修改时间。"),
            new StrategyChoice(SortStrategy.Natural, "自然排序", "从左到右比较，数字按数值而不是字典序。")
        ];
        foreach (var choice in Strategies)
            choice.Select = () => Strategy = choice.Strategy;
        SyncStrategyButtons();
        StatusText = _statusBase;
    }

    public IReadOnlyList<StrategyChoice> Strategies { get; }

    [ObservableProperty]
    private string _folderPath = "";

    [ObservableProperty]
    private SortStrategy _strategy = SortStrategy.Auto;

    [ObservableProperty]
    private bool _descending;

    [ObservableProperty]
    private bool _stripResolution = true;

    [ObservableProperty]
    private bool _stripDatesAndLongIds = true;

    [ObservableProperty]
    private bool _ignoreConstantSegments = true;

    [ObservableProperty]
    private string _prefixDelimiter = "_";

    [ObservableProperty]
    private int _prefixSkipCount = 1;

    [ObservableProperty]
    private string _customPattern = @"^\d+_(\d+)";

    [ObservableProperty]
    private bool _regexIgnoreCase = true;

    [ObservableProperty]
    private int _renamePadWidth = 3;

    [ObservableProperty]
    private bool _imagesOnly = true;

    [ObservableProperty]
    private string _nameFilter = "";

    [ObservableProperty]
    private string _statusText = "";

    [ObservableProperty]
    private string _patternError = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasVisibleFiles;

    [ObservableProperty]
    private string _emptyHint = "把图片文件夹拖到窗口里，或点击浏览。";

    [ObservableProperty]
    private ObservableCollection<FileRowViewModel> _visibleFiles = [];

    [ObservableProperty]
    private FileRowViewModel? _selectedFile;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    private string _previewHint = "选中一张图片后在这里预览。";

    [ObservableProperty]
    private string _renameButtonText = "规范化重命名";

    public bool ShowPrefixOptions => Strategy == SortStrategy.PrefixStrip;

    public bool ShowRegexOptions => Strategy == SortStrategy.CustomRegex;

    public bool ShowMetadataHint => Strategy == SortStrategy.Metadata;

    public bool ShowNoiseOptions => Strategy is SortStrategy.Auto or SortStrategy.LastNumber or SortStrategy.Natural or SortStrategy.PrefixStrip;

    public string StrategyDescription => Strategies.First(choice => choice.Strategy == Strategy).Description;

    public int[] SkipCounts { get; } = [0, 1, 2, 3, 4, 5, 6, 8];

    public int[] PadWidths { get; } = [1, 2, 3, 4, 5, 6, 8];

    public string RenameHint => "有明确序号时保留该数字并补零，例如 10 会变成 010；否则按当前顺序从 1 编号。";

    partial void OnStrategyChanged(SortStrategy value)
    {
        SyncStrategyButtons();
        NotifyModeVisibility();
        RequestRebuild();
    }

    partial void OnDescendingChanged(bool value) => RequestRebuild();

    partial void OnStripResolutionChanged(bool value) => RequestRebuild();

    partial void OnStripDatesAndLongIdsChanged(bool value) => RequestRebuild();

    partial void OnIgnoreConstantSegmentsChanged(bool value) => RequestRebuild();

    partial void OnPrefixDelimiterChanged(string value) => RequestRebuild();

    partial void OnPrefixSkipCountChanged(int value) => RequestRebuild();

    partial void OnRegexIgnoreCaseChanged(bool value) => RequestRebuild();

    partial void OnRenamePadWidthChanged(int value) => RequestRebuild();

    partial void OnCustomPatternChanged(string value) => _ = RebuildAsync(debounce: true);

    partial void OnNameFilterChanged(string value) => ApplyFilter();

    partial void OnImagesOnlyChanged(bool value)
    {
        if (!string.IsNullOrWhiteSpace(FolderPath) && Directory.Exists(FolderPath))
            _ = LoadFolderAsync();
    }

    partial void OnIsBusyChanged(bool value) => RenameCommand.NotifyCanExecuteChanged();

    partial void OnSelectedFileChanged(FileRowViewModel? value)
    {
        var generation = ++_previewGeneration;
        var previous = Preview;
        Preview = null;
        previous?.Dispose();
        if (value is null)
        {
            PreviewHint = "选中一张图片后在这里预览。";
            return;
        }

        PreviewHint = "正在读取预览…";
        _ = LoadPreviewAsync(value.FullPath, generation);
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var path = await _dialogs.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;
        FolderPath = path;
        await LoadFolderAsync();
    }

    [RelayCommand]
    private Task LoadFolderAsync() => LoadFolderCoreAsync(FolderPath);

    public void OpenPath(string path)
    {
        if (File.Exists(path))
            path = Path.GetDirectoryName(path) ?? path;
        if (!Directory.Exists(path))
            return;
        FolderPath = path;
        _ = LoadFolderAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync()
    {
        var changes = _plan.Where(item => !item.Unchanged).ToList();
        if (changes.Count == 0)
            return;
        if (!await _dialogs.ConfirmRenameAsync(changes))
            return;

        IsBusy = true;
        try
        {
            var plan = _plan.ToList();
            var result = await Task.Run(() => RenameExecutor.Execute(plan));
            if (result.Error is not null)
                await _dialogs.AlertAsync("重命名没有全部完成", result.Error);
            await LoadFolderCoreAsync(FolderPath);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenSelectedAsync()
    {
        if (SelectedFile is null || !File.Exists(SelectedFile.FullPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(SelectedFile.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("无法打开", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RevealFolderAsync()
    {
        if (!Directory.Exists(FolderPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(FolderPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await _dialogs.AlertAsync("无法打开文件夹", ex.Message);
        }
    }

    private bool CanRename() => !IsBusy && _pendingRenames > 0;

    private void RequestRebuild() => _ = RebuildAsync(debounce: false);

    private async Task RebuildAsync(bool debounce)
    {
        if (debounce)
        {
            _patternDebounce?.Cancel();
            var debounceSource = new CancellationTokenSource();
            _patternDebounce = debounceSource;
            try
            {
                await Task.Delay(200, debounceSource.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var generation = ++_generation;
        var options = SnapshotOptions();
        var cache = _cache.ToArray();
        SortResult result;
        try
        {
            result = await Task.Run(() => SmartImageSorter.Sort(cache, options));
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                StatusText = "排序失败：" + ex.Message;
            return;
        }

        if (generation != _generation)
            return;

        if (result.PatternError is not null)
        {
            PatternError = result.PatternError;
            return;
        }

        PatternError = "";
        var selectedPath = SelectedFile?.FullPath;
        var pad = Math.Clamp(RenamePadWidth, 1, 12);
        var plan = RenamePlanner.Plan(result.Files, pad, OccupiedBySomeoneElse);
        _plan = plan.ToList();
        _rows = result.Files.Select((file, index) => new FileRowViewModel(file, plan[index], index + 1)).ToList();
        _pendingRenames = _plan.Count(item => !item.Unchanged);
        RenameButtonText = _pendingRenames == 0 ? "名称已符合当前顺序" : $"规范化重命名（{_pendingRenames}）";
        RenameCommand.NotifyCanExecuteChanged();
        _statusBase = BuildStatus(result);
        EmptyHint = _rows.Count == 0
            ? (ImagesOnly ? "这个文件夹里没有支持的图片。" : "这个文件夹是空的。")
            : "没有符合筛选的文件。";
        ApplyFilter();
        SelectedFile = _rows.FirstOrDefault(row => row.FullPath == selectedPath)
            ?? VisibleFiles.FirstOrDefault();
    }

    private async Task LoadFolderCoreAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            StatusText = "文件夹不存在。";
            return;
        }

        IsBusy = true;
        try
        {
            var imagesOnly = ImagesOnly;
            var reader = _times;
            var progress = new Progress<string>(text => StatusText = text);
            var inputs = await Task.Run(() => Scan(path, imagesOnly, reader, progress));
            _cache.Clear();
            _cache.AddRange(inputs);
            await RebuildAsync(debounce: false);
        }
        catch (Exception ex)
        {
            StatusText = "读取失败：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static List<FileSortInput> Scan(
        string path,
        bool imagesOnly,
        ICapturedTimeReader reader,
        IProgress<string> progress)
    {
        var files = Directory.EnumerateFiles(path)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith('.'))
                    return false;
                if (name.EndsWith(".tzjtmp", StringComparison.OrdinalIgnoreCase))
                    return false;
                return !imagesOnly || ImageExtensions.Contains(Path.GetExtension(file));
            })
            .ToList();

        var results = new FileSortInput[files.Count];
        var done = 0;
        Parallel.For(
            0,
            files.Count,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            index =>
            {
                var file = files[index];
                DateTime? exif = null;
                try
                {
                    exif = reader.ReadOriginalTime(file);
                }
                catch (Exception)
                {
                    exif = null;
                }

                var info = new FileInfo(file);
                results[index] = new FileSortInput(info.FullName, info.Name, info.LastWriteTime, exif);
                var count = Interlocked.Increment(ref done);
                if (count == files.Count || count % 25 == 0)
                    progress.Report($"正在读取 {count}/{files.Count}");
            });

        return results.Where(item => item is not null).ToList();
    }

    private SortOptions SnapshotOptions() => new()
    {
        Strategy = Strategy,
        Direction = Descending ? SortDirection.Descending : SortDirection.Ascending,
        StripResolution = StripResolution,
        StripDatesAndLongIds = StripDatesAndLongIds,
        IgnoreConstantSegments = IgnoreConstantSegments,
        PrefixDelimiter = PrefixDelimiter,
        PrefixSkipCount = PrefixSkipCount,
        CustomPattern = CustomPattern,
        RegexIgnoreCase = RegexIgnoreCase,
        RenamePadWidth = RenamePadWidth
    };

    private bool OccupiedBySomeoneElse(string path)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var file in _cache)
        {
            if (comparer.Equals(file.FullPath, path))
                return false;
        }

        return File.Exists(path);
    }

    private string BuildStatus(SortResult result)
    {
        var text = $"{result.Files.Count} 个文件 · {Strategies.First(choice => choice.Strategy == Strategy).Title}";
        if (result.IgnoredConstantValues.Count > 0)
            text += " · 已忽略全文件夹相同的数字段 " + string.Join("、", result.IgnoredConstantValues);
        return text;
    }

    private void ApplyFilter()
    {
        var filter = NameFilter.Trim();
        IEnumerable<FileRowViewModel> rows = _rows;
        if (filter.Length > 0)
            rows = rows.Where(row => row.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase));

        VisibleFiles = new ObservableCollection<FileRowViewModel>(rows);
        HasVisibleFiles = VisibleFiles.Count > 0;
        StatusText = filter.Length == 0
            ? _statusBase
            : _statusBase + " · 筛选只影响列表，重命名仍作用于全部文件";
    }

    private async Task LoadPreviewAsync(string path, int generation)
    {
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return Bitmap.DecodeToWidth(stream, 480);
            });
            if (generation != _previewGeneration)
            {
                bitmap.Dispose();
                return;
            }

            Preview = bitmap;
            PreviewHint = SelectedFile?.Explanation ?? "";
        }
        catch (Exception)
        {
            if (generation == _previewGeneration)
                PreviewHint = "无法预览这个文件";
        }
    }

    private void SyncStrategyButtons()
    {
        foreach (var choice in Strategies)
            choice.SetSelected(choice.Strategy == Strategy);
    }

    private void NotifyModeVisibility()
    {
        OnPropertyChanged(nameof(ShowPrefixOptions));
        OnPropertyChanged(nameof(ShowRegexOptions));
        OnPropertyChanged(nameof(ShowMetadataHint));
        OnPropertyChanged(nameof(ShowNoiseOptions));
        OnPropertyChanged(nameof(StrategyDescription));
    }
}
