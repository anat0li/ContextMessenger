using System.Text;
using System.Text.RegularExpressions;

namespace ContextMessenger.Data;

public sealed class ReadOnlySqlGuard : IReadOnlySqlGuard
{
    private static readonly Regex ForbiddenTokenRegex = new(
        @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|EXEC|EXECUTE|CALL|GRANT|REVOKE)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ReadOnlySqlException("SQL text is required.");
        }

        var stripped = StripComments(sql).Trim();
        if (stripped.Length == 0)
        {
            throw new ReadOnlySqlException("SQL text is required.");
        }

        if (ContainsStatementSeparator(stripped))
        {
            throw new ReadOnlySqlException("Only a single read-only statement is allowed.");
        }

        if (ForbiddenTokenRegex.IsMatch(stripped))
        {
            throw new ReadOnlySqlException("Only read-only SQL statements are allowed.");
        }

        if (!StartsWithReadOnlyStatement(stripped))
        {
            throw new ReadOnlySqlException("SQL must start with SELECT, WITH, or EXPLAIN.");
        }
    }

    private static bool StartsWithReadOnlyStatement(string sql)
    {
        var firstToken = ReadFirstToken(sql);
        if (firstToken.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (firstToken.Equals("EXPLAIN", StringComparison.OrdinalIgnoreCase))
        {
            return Regex.IsMatch(sql, @"^\s*EXPLAIN\s+(QUERY\s+PLAN\s+)?SELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return firstToken.Equals("WITH", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(sql, @"\bSELECT\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ReadFirstToken(string sql)
    {
        var match = Regex.Match(sql, @"^\s*([A-Za-z]+)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "";
    }

    private static bool ContainsStatementSeparator(string sql)
    {
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];
            if (current == '\'' && !inDoubleQuote)
            {
                if (inSingleQuote && i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
            }
            else if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (current == ';' && !inSingleQuote && !inDoubleQuote)
            {
                return sql[(i + 1)..].Trim().Length > 0;
            }
        }

        return false;
    }

    private static string StripComments(string sql)
    {
        var output = new StringBuilder(sql.Length);
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (current == '\'' && !inDoubleQuote)
            {
                output.Append(current);
                if (inSingleQuote && next == '\'')
                {
                    output.Append(next);
                    i++;
                    continue;
                }

                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                output.Append(current);
                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && current == '-' && next == '-')
            {
                while (i < sql.Length && sql[i] != '\r' && sql[i] != '\n')
                {
                    i++;
                }

                output.Append(' ');
                if (i < sql.Length)
                {
                    output.Append(sql[i]);
                }

                continue;
            }

            if (!inSingleQuote && !inDoubleQuote && current == '/' && next == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    i++;
                }

                i++;
                output.Append(' ');
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }
}
