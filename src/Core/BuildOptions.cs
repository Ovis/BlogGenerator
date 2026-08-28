namespace BlogGenerator.Core;

internal sealed record BuildOptions(
    string InputPath,
    string OutputPath,
    string ThemePath,
    string? OEmbedCachePath,
    string? AmazonCachePath,
    FileInfo? ConfigFile);
