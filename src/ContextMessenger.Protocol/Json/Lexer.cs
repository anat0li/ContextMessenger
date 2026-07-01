using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace ContextMessenger.Protocol.Json;

public class Lexer
{
    private readonly string _source;
    private int _current;
    private int _start;
    private int _line = 1;

    private static readonly Dictionary<string, TokenType> _keywords = new()
    {
        {"null", TokenType.Null},
        {"true", TokenType.True},
        {"false", TokenType.False},
    };

    /// <summary>
    /// Property names whose string values are treated as newline-terminated, so that
    /// unescaped quotes embedded in the value are folded in as literal characters.
    /// These are the protocol's free-text fields that Claude emits one value per line;
    /// single-line fields are deliberately excluded because their values are not
    /// guaranteed to end at a line break.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultTerminatedKeys = new[]
    {
        "anchor", "text", "oldText", "newText",
        "newContent", "description", "commitMessage", "title",
    };

    private readonly HashSet<string> _terminatedKeys;

    public Lexer(string source)
        : this(source, DefaultTerminatedKeys)
    {
    }

    public Lexer(string source, IEnumerable<string> terminatedKeys)
    {
        _source = source.Trim();
        _terminatedKeys = new HashSet<string>(terminatedKeys, StringComparer.Ordinal);
    }

    public static string Escape(string input)
    {
        var lexer = new Lexer(input);
        var tokens = lexer.Tokenize();
        return Token.ToString(tokens);
    }

    public Token NextToken(bool terminate)
    {
        while (_current < _source.Length)
        {
            char ch = _source[_current++];
            if (char.IsWhiteSpace(ch))
            {
                if (ch == '\n')
                    _line++;
                continue;
            }
            _start = _current - 1;
            return ch switch
            {
                '{' => new Token(TokenType.LeftBrace, "{", null, _line),
                '}' => new Token(TokenType.RightBrace, "}", null, _line),
                '[' => new Token(TokenType.LeftParen, "[", null, _line),
                ']' => new Token(TokenType.RightParen, "]", null, _line),
                ',' => new Token(TokenType.Comma, ",", null, _line),
                ':' => new Token(TokenType.Colon, ":", null, _line),
                '"' => ReadString(terminate),
                _ when char.IsAsciiLetterLower(ch) => ReadKeyword(),
                _ when char.IsDigit(ch) || ch == '-' => ReadNumber(),
                _ => throw new Exception($"Unexpected character '{ch}' at line {_line}")
            };
        }
        return new Token(TokenType.Eof, "", null, _line);
    }

    public IReadOnlyList<Token> Tokenize()
    {
        var tokens = new List<Token>();
        AddValueTokens(NextToken(false), tokens);
        Add(NextToken(false), TokenType.Eof, tokens);
        return tokens;
    }

    private void AddObjectTokens(Token token, IList<Token> tokens)
    {
        Add(token, TokenType.LeftBrace, tokens);
        token = NextToken(false);
        if (token.Type != TokenType.RightBrace)
        {
            while (true)
            {
                Add(token, TokenType.String, tokens);
                string key = (string)token.Literal!;
                Add(NextToken(false), TokenType.Colon, tokens);
                AddValueTokens(NextToken(_terminatedKeys.Contains(key)), tokens);
                token = NextToken(false);
                if (token.Type != TokenType.Comma)
                    break;
                tokens.Add(token);
                token = NextToken(false);
            }
        }
        Add(token, TokenType.RightBrace, tokens);
    }

    private void AddArrayTokens(Token token, IList<Token> tokens)
    {
        Add(token, TokenType.LeftParen, tokens);
        token = NextToken(false);
        if (token.Type != TokenType.RightParen)
        {
            while (true)
            {
                AddValueTokens(token, tokens);
                token = NextToken(false);
                if (token.Type != TokenType.Comma)
                    break;
                tokens.Add(token);
                token = NextToken(false);
            }
        }
        Add(token, TokenType.RightParen, tokens);
    }

    private void AddValueTokens(Token token, IList<Token> tokens)
    {
        switch (token.Type)
        {
            case TokenType.LeftBrace:
                AddObjectTokens(token, tokens);
                return;

            case TokenType.LeftParen:
                AddArrayTokens(token, tokens);
                return;

            case TokenType.String:
            case TokenType.Number:
            case TokenType.True:
            case TokenType.False:
            case TokenType.Null:
                if (tokens.Count == 0)
                    break;
                tokens.Add(token);
                return;    
        }
        throw new ProtocolException(ProtocolErrorCodes.InvalidJson, ErrorMessage($"Unexpected value token {token.Type}"));
    }

    private void Add(Token token, TokenType expected, IList<Token> tokens)
    {
        if (token.Type != expected)
            throw new ProtocolException(ProtocolErrorCodes.InvalidJson, ErrorMessage($"Expected {expected} but got {token.Type}"));
        tokens.Add(token);
    }

    private Token ReadKeyword()
    {
        while (_current < _source.Length && char.IsAsciiLetterLower(_source[_current]))
        {
            _current++;
        }
        string text = _source[_start.._current];
        if (!_keywords.TryGetValue(text, out TokenType type))
            throw new ProtocolException(ProtocolErrorCodes.InvalidJson, ErrorMessage($"Unexpected keyword '{text}'"));
        return new Token(type, text, type == TokenType.Null ? null : bool.Parse(text), _line);
    }

    private Token ReadNumber()
    {
        // A JSON number is always a value, so it ends at the first whitespace or
        // structural punctuation. Scan to that boundary and let double.TryParse
        // validate the grammar (sign, decimal, exponent) and pass the original
        // lexeme through verbatim.
        while (_current < _source.Length && !IsNumberTerminator(_source[_current]))
        {
            _current++;
        }
        string text = _source[_start.._current];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ProtocolException(ProtocolErrorCodes.InvalidJson, ErrorMessage($"Invalid number '{text}'"));
        return new Token(TokenType.Number, text, value, _line);
    }

    private static bool IsNumberTerminator(char ch) =>
        char.IsWhiteSpace(ch) || ch is ',' or ']' or '}';

    private Token ReadString(bool terminate)
    {
        var sb = new StringBuilder();
        while (_current < _source.Length)
        {
            char ch = _source[_current++];
            if (char.IsWhiteSpace(ch))
            {
                if (ch == '\n')
                    _line++;
                sb.Append(ch);
                continue;
            }
            if (ch == '\\' && _current < _source.Length)
            {
                sb.Append(Regex.Unescape("\\" + _source[_current++]));
                continue;
            }
            if (ch != '"')
            {
                sb.Append(ch);
                continue;
            }

            bool IsTerminated(int i)
            {
                if (i == _source.Length)
                    return false;
                if (_source[i] == ']')
                    i++;
                if (_source[i] == ',')
                    i++;
                if (_source[i] == '\r')
                    i++;
                return _source[i] == '\n';
            }
            if (terminate && !IsTerminated(_current))
            {
                sb.Append(ch);
                continue;
            }

            string text = sb.ToString();
            return new Token(TokenType.String, HttpUtility.JavaScriptStringEncode(text), text, _line);
        }
        throw new ProtocolException(ProtocolErrorCodes.InvalidJson, ErrorMessage("Unterminated string"));
    }

    private string ErrorMessage(string message)
        => $"Request JSON is invalid: [line {_line}] {message}. If embedding source code in newContent, escape quotes as \\\" or use newContentEncoding: \"base64utf8\" / \"gzipbase64utf8\".";
}
