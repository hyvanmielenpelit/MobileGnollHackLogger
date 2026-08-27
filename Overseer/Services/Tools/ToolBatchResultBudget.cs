namespace Overseer.Services.Tools;

public sealed class ToolBatchResultBudget
{
    private readonly int _budget;
    private int _used;

    public ToolBatchResultBudget(int budget) => _budget = Math.Max(1, budget);
    public bool AnyTruncated { get; private set; }

    public string Apply(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        if (_used >= _budget)
        {
            AnyTruncated = true;
            return "(skipped: batch output budget reached)";
        }

        int remaining = _budget - _used;
        if (content.Length <= remaining)
        {
            _used += content.Length;
            return content;
        }

        AnyTruncated = true;
        _used = _budget;
        return content.Substring(0, remaining) + "\n\n... (truncated: batch output budget reached)";
    }
}