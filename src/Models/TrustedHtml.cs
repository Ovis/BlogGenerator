namespace BlogGenerator.Models;

/// <summary>
/// テンプレートで明示的に生出力してよいHTMLを表す
/// </summary>
public sealed class TrustedHtml
{
    public static TrustedHtml Empty { get; } = new(string.Empty);

    public TrustedHtml(string value)
    {
        Value = value ?? string.Empty;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
