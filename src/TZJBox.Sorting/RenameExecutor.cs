namespace TZJBox.Sorting;

public static class RenameExecutor
{
    public static RenameExecutionResult Execute(IReadOnlyList<RenamePlanItem> plan)
    {
        var pending = plan.Where(item => !item.Unchanged).ToList();
        if (pending.Count == 0)
            return new RenameExecutionResult();

        var moved = new List<Step>();
        try
        {
            foreach (var item in pending)
            {
                var directory = Path.GetDirectoryName(item.FinalPath);
                if (string.IsNullOrEmpty(directory))
                    directory = Path.GetDirectoryName(Path.GetFullPath(item.SourcePath)) ?? ".";

                var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tzjtmp");
                File.Move(item.SourcePath, temp);
                moved.Add(new Step(item.SourcePath, temp, item.FinalPath, false));
            }

            for (var index = 0; index < moved.Count; index++)
            {
                var step = moved[index];
                File.Move(step.Temp, step.Final);
                moved[index] = step with { Finalized = true };
            }

            return new RenameExecutionResult { Renamed = moved.Count };
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            foreach (var step in moved)
            {
                if (step.Finalized || !File.Exists(step.Temp))
                    continue;

                try
                {
                    if (!File.Exists(step.Source))
                        File.Move(step.Temp, step.Source);
                }
                catch (Exception rollback)
                {
                    rollbackErrors.Add(rollback.Message);
                }
            }

            var message = ex.Message;
            if (rollbackErrors.Count > 0)
                message += " 回滚时也出现问题：" + string.Join("；", rollbackErrors);

            return new RenameExecutionResult
            {
                Renamed = moved.Count(step => step.Finalized),
                Error = message
            };
        }
    }

    private sealed record Step(string Source, string Temp, string Final, bool Finalized);
}
