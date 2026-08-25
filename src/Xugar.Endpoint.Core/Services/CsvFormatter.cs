using System.Text;

namespace Xugar.Endpoint.Core.Services;

public static class CsvFormatter
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains(',') &&
            !value.Contains('"') &&
            !value.Contains('\r') &&
            !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    public static string CreateRow(params string?[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return string.Join(',', fields.Select(Escape));
    }

    public static IReadOnlyList<IReadOnlyList<string>> ParseRows(string csv)
    {
        ArgumentNullException.ThrowIfNull(csv);

        if (csv.Length > 0 && csv[0] == '\uFEFF')
        {
            csv = csv[1..];
        }

        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var rowHasContent = false;

        for (var index = 0; index < csv.Length; index++)
        {
            var character = csv[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < csv.Length && csv[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(character);
                }

                rowHasContent = true;
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    inQuotes = true;
                    rowHasContent = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    rowHasContent = true;
                    break;
                case '\r':
                case '\n':
                    if (character == '\r' && index + 1 < csv.Length && csv[index + 1] == '\n')
                    {
                        index++;
                    }

                    row.Add(field.ToString());
                    field.Clear();
                    if (rowHasContent || row.Count > 1)
                    {
                        rows.Add(row.ToArray());
                    }

                    row = [];
                    rowHasContent = false;
                    break;
                default:
                    field.Append(character);
                    rowHasContent = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new FormatException("CSV ended inside a quoted field.");
        }

        if (rowHasContent || row.Count > 0 || field.Length > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
