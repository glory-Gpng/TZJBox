using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace TZJBox.App.Services;

/// <summary>
/// 读取拍摄时间。故意不读 CreationTime：创建时间通常是下载完成的顺序，正是把图包打乱的那个时间。
/// </summary>
public sealed class ExifCapturedTimeReader : ICapturedTimeReader
{
    public DateTime? ReadOriginalTime(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var sub = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (sub is not null)
            {
                if (TryRead(sub, ExifDirectoryBase.TagDateTimeOriginal, out var original))
                    return original;
                if (TryRead(sub, ExifDirectoryBase.TagDateTimeDigitized, out var digitized))
                    return digitized;
            }

            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null && TryRead(ifd0, ExifDirectoryBase.TagDateTime, out var fallback))
                return fallback;
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private static bool TryRead(MetadataExtractor.Directory directory, int tag, out DateTime value)
    {
        try
        {
            return directory.TryGetDateTime(tag, out value);
        }
        catch (Exception)
        {
            value = default;
            return false;
        }
    }
}
