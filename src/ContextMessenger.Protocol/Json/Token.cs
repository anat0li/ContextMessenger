using System.Text;

namespace ContextMessenger.Protocol.Json;

public readonly record struct Token(TokenType Type, string Lexeme, object? Literal, int Line)
{
    public override string ToString() => Type + " " + Lexeme + " " + Literal;

    public static string ToString(IEnumerable<Token> tokens)
    {
        var sb = new StringBuilder();
        foreach (var token in tokens)
        {
            if (sb.Length > 0)
                sb.Append(' ');
            if (token.Type is TokenType.Identifier or TokenType.String)
                sb.Append('"').Append(token.Lexeme).Append('"');
            else
                sb.Append(token.Lexeme);
        }
        return sb.ToString();
    }
}