using System.Data;
using System.Text;

namespace AlSaqr.Accounting;

internal static class TableSearchFilter
{
    public static void Apply(DataView view, string query)
    {
        ArgumentNullException.ThrowIfNull(view);

        if (string.IsNullOrWhiteSpace(query))
        {
            view.RowFilter = string.Empty;
            return;
        }

        var pattern = "%" + EscapeLikeLiteral(query.Trim()).Replace("'", "''") + "%";
        var expressions = view.Table.Columns
            .Cast<DataColumn>()
            .Select(column => $"Convert({QuoteIdentifier(column.ColumnName)}, 'System.String') LIKE '{pattern}'")
            .ToArray();

        view.RowFilter = expressions.Length == 0
            ? string.Empty
            : string.Join(" OR ", expressions);
    }

    private static string QuoteIdentifier(string name) =>
        "[" + name.Replace("]", "]]") + "]";

    private static string EscapeLikeLiteral(string value)
    {
        var result = new StringBuilder(value.Length + 8);
        foreach (var character in value)
        {
            result.Append(character switch
            {
                '[' => "[[]",
                ']' => "[]]",
                '%' => "[%]",
                '*' => "[*]",
                _ => character.ToString()
            });
        }
        return result.ToString();
    }
}