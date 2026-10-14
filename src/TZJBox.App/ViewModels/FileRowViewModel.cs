using TZJBox.Sorting;

namespace TZJBox.App.ViewModels;

public sealed class FileRowViewModel
{
    public FileRowViewModel(SortedFile file, RenamePlanItem plan, int index)
    {
        Index = index;
        FullPath = file.Source.FullPath;
        FileName = file.FileName;
        KeyLabel = file.KeyLabel;
        Explanation = file.Explanation;
        ProposedName = plan.ProposedFileName;
        NameChanges = !plan.Unchanged;
        Spans = FileNameHighlighter.Split(file.FileName, file.Highlights);
        ExifText = file.Source.ExifTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "无";
        WriteText = file.Source.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public int Index { get; }

    public string FullPath { get; }

    public string FileName { get; }

    public string KeyLabel { get; }

    public string Explanation { get; }

    public string ProposedName { get; }

    public bool NameChanges { get; }

    public IReadOnlyList<NameSpan> Spans { get; }

    public string ExifText { get; }

    public string WriteText { get; }
}
