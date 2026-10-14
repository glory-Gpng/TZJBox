namespace TZJBox.App.Services;

public interface ICapturedTimeReader
{
    DateTime? ReadOriginalTime(string path);
}
