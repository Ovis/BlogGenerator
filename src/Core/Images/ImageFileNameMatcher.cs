using System.Buffers;

namespace BlogGenerator.Core.Images;

/// <summary>残存するファイル名を、構文を仮定せず複数文字列検索で検出する。</summary>
internal sealed class ImageFileNameMatcher
{
    private readonly string[] _names;
    private readonly SearchValues<string> _values;

    public ImageFileNameMatcher(IEnumerable<string> fileNames)
    {
        _names = fileNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _values = SearchValues.Create(_names, StringComparison.OrdinalIgnoreCase);
    }

    public void AddMatches(string text, HashSet<string> matches)
    {
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var index = remaining.IndexOfAny(_values);
            if (index < 0) break;
            remaining = remaining[index..];
            foreach (var name in _names)
                if (!matches.Contains(name) && remaining.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    matches.Add(name);
            // 一致の長さだけ進めると、重なる別の名前を見逃すため1文字ずつ進める。
            remaining = remaining[1..];
        }
    }
}
