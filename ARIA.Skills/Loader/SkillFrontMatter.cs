namespace ARIA.Skills.Loader;

public sealed record SkillFrontMatter(string? Name, string? Description);

public static class SkillFrontMatterParser
{
    public static SkillFrontMatter Parse(string content)
    {
        using var reader = new StringReader(content);
        var firstLine = reader.ReadLine();
        if (!string.Equals(firstLine?.Trim(), "---", StringComparison.Ordinal))
            return new SkillFrontMatter(null, null);

        string? name = null;
        string? description = null;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                return new SkillFrontMatter(name, description);

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());

            if (string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                name = value;
            else if (string.Equals(key, "description", StringComparison.OrdinalIgnoreCase))
                description = value;
        }

        return new SkillFrontMatter(null, null);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
