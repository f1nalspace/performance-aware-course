using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Final.PerformanceAwareCourse
{
    public readonly struct Error
    {
        public string Message { get; }

        public Error(string message)
        {
            Message = message;
        }

        public Error(string message, Error error)
        {
            Message = $"{message}: {error}";
        }

        public override string ToString() => Message;
    }

    public readonly struct Result<T>
    {
        public T Value { get; }
        public Error Error { get; }
        public bool Success { get; }

        public Result(T value)
        {
            Error = default;
            Value = value;
            Success = true;
        }

        public Result(Error error)
        {
            Error = error;
            Value = default;
            Success = false;
        }

        public static implicit operator Result<T>(T value) => new Result<T>(value);
        public static implicit operator Result<T>(Error error) => new Result<T>(error);
    }

    public struct JSONLocation
    {
        public int Position { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public JSONLocation(int position, int line, int column) : this()
        {
            Position = position;
            Line = line;
            Column = column;
        }

        public JSONLocation AddColumns(int len) => new JSONLocation(Position + len, Line, Column + len);

        public override string ToString() => $"Ln: {Line}, Col: {Column}, Pos: {Position}";
    }

    enum JSONTokenKind : int
    {
        Invalid = 0,
        OpenObjectOp,
        CloseObjectOp,
        OpenArrayOp,
        CloseArrayOp,
        AssignmentOp,
        SeparatorOp,
        IntegerLiteral,
        DecimalLiteral,
        FalseLiteral,
        TrueLiteral,
        NullLiteral,
        StringLiteral,
    }

    class JSONToken
    {
        public JSONTokenKind Kind { get; }
        public JSONLocation Start { get; }
        public JSONLocation End { get; }
        public int Length => End.Position - Start.Position;
        public string Text { get; internal set; }

        public double NumberLiteral { get; }
        public string StringLiteral { get; }
        public char OperatorChar { get; }


        public JSONToken(JSONTokenKind kind, JSONLocation start, JSONLocation end)
        {
            Kind = kind;
            Start = start;
            End = end;
        }

        public JSONToken(JSONTokenKind kind, JSONLocation start, JSONLocation end, char opChar) : this(kind, start, end)
        {
            OperatorChar = opChar;
            Text = opChar.ToString();
        }

        public JSONToken(JSONTokenKind kind, JSONLocation start, JSONLocation end, double numberLiteral) : this(kind, start, end)
        {
            NumberLiteral = numberLiteral;
        }

        public JSONToken(JSONTokenKind kind, JSONLocation start, JSONLocation end, string stringLiteral) : this(kind, start, end)
        {
            StringLiteral = stringLiteral;
        }

        public override string ToString() => $"[Location: {Start}, Len: {Length}, Kind: {Kind}] '{Text}'";
    }

    public enum JSONElementKind
    {
        None = 0,
        Root,
        Object,
        Array,
        String,
        Number,
        Boolean,
        Null,
    }

    public class JSONElement
    {
        public JSONElementKind Kind { get; }
        public JSONLocation Location { get; }
        public string Label { get; }

        public int ChildCount => _children.Count;
        public IEnumerable<JSONElement> Children => _children;

        public string StringValue { get; }
        public double NumberValue { get; }
        public bool BooleanValue { get; }

        public string Value
        {
            get
            {
                return Kind switch
                {
                    JSONElementKind.String => StringValue,
                    JSONElementKind.Number => NumberValue.ToString(CultureInfo.InvariantCulture),
                    JSONElementKind.Boolean => BooleanValue ? "true" : "false",
                    JSONElementKind.Null => "null",
                    _ => null,
                };
            }
        }

        private readonly List<JSONElement> _children = new List<JSONElement>();

        public JSONElement(JSONElementKind kind, JSONLocation location, string label)
        {
            Kind = kind;
            Label = label;
        }

        public JSONElement(JSONLocation location, string label, string stringValue) : this(JSONElementKind.String, location, label)
        {
            StringValue = stringValue;
        }

        public JSONElement(JSONLocation location, string label, double numberValue) : this(JSONElementKind.Number, location, label)
        {
            NumberValue = numberValue;
        }

        public JSONElement(JSONLocation location, string label, bool boolValue) : this(JSONElementKind.Boolean, location, label)
        {
            BooleanValue = boolValue;
        }

        public void AddChild(JSONElement element) => _children.Add(element);

        public IEnumerable<string> GetLabels() => _children.Select(s => s.Label);
        public JSONElement FindByLabel(string label) => _children.FirstOrDefault(s => string.Equals(s.Label, label));

        public override string ToString() => $"{Kind} => {Value} [{Label}] at {Location}";
    }

    public class JSONParser
    {

        static bool StreamHasData(ReadOnlySpan<byte> stream, int minLen = 1) => stream.Length >= minLen;

        static bool CharIsNumber(byte ch) => ch >= '0' && ch <= '9';

        static bool CharIsWhitespace(byte ch) =>
            ch == ' ' ||
            ch == '\t' ||
            ch == '\b' ||
            ch == '\f' ||
            ch == '\r' ||
            ch == '\n';

        static string GetText(ReadOnlySpan<byte> stream, int len)
        {
            if (stream.Length >= len)
            {
                ReadOnlySpan<byte> s = stream.Slice(0, len);
                StringBuilder result = new StringBuilder(len);
                for (int i = 0; i < s.Length; ++i)
                    result.Append((char)s[i]);
                return result.ToString();
            }
            return null;
        }

        static string GetText(ReadOnlySpan<byte> stream, JSONLocation start, JSONLocation end)
        {
            int len = end.Position - start.Position;
            return GetText(stream, len);
        }

        const int ColumnsPerTab = 4;

        static bool UpdateLocationFromWhitespace(ref JSONLocation location, char ch)
        {
            switch (ch)
            {
                case '\n':
                    location.Line++;
                    location.Column = 0;
                    location.Position++;
                    return true;
                case '\b':
                case '\f':
                case '\r':
                case ' ':
                    location.Column++;
                    location.Position++;
                    return true;
                case '\t':
                    location.Column += ColumnsPerTab;
                    location.Position++;
                    return true;
                default:
                    return false;
            }
        }

        static void SkipWhitespaces(ref JSONLocation location, ref ReadOnlySpan<byte> stream)
        {
            ReadOnlySpan<byte> cur = stream;
            char c;
            while (cur.Length > 0)
            {
                c = (char)cur[0];
                if (!UpdateLocationFromWhitespace(ref location, c))
                    break;
                cur = cur.Slice(1);
            }
            stream = cur;
        }

        static readonly Error EndOfStreamError = new Error("End of stream");

        static Result<JSONToken> GetNumberToken(JSONLocation location, ReadOnlySpan<byte> stream, bool hasSign)
        {
            if (!StreamHasData(stream))
                return EndOfStreamError;

            ReadOnlySpan<byte> cur = stream;

            byte c = cur[0];

            double factor = 1.0;
            if (hasSign)
            {
                Debug.Assert(c == '-');
                factor = c == '-' ? -1.0 : 1.0;
                cur = cur.Slice(1);
            }
            else
                Debug.Assert(c >= '0' && c <= '9');

            if (!StreamHasData(cur))
                return EndOfStreamError;
            if (!CharIsNumber(c = cur[0]))
                return new Error($"Invalid number literal character '{c}' at location '{location}'");

            double number = 0;
            while (StreamHasData(cur) && CharIsNumber(c = cur[0]))
            {
                int i = c - '0';
                number = number * 10.0 + (double)i;
                cur = cur.Slice(1);
            }

            JSONTokenKind kind = JSONTokenKind.IntegerLiteral;
            if (StreamHasData(cur) && (c = cur[0]) == '.')
            {
                kind = JSONTokenKind.DecimalLiteral;
                cur = cur.Slice(1);
                while (StreamHasData(cur) && CharIsNumber(c = cur[0]))
                {
                    int i = c - '0';
                    factor /= 10.0;
                    number = number * 10.0 + (double)i;
                    cur = cur.Slice(1);
                }
            }

            // TODO(final): Scientific notation support

            int len = stream.Length - cur.Length;

            JSONLocation end = location.AddColumns(len);

            double numberLiteral = number * factor;

            return new JSONToken(kind, location, end, numberLiteral) { Text = GetText(stream, location, end) };
        }

        static Result<JSONToken> GetStringToken(JSONLocation location, ReadOnlySpan<byte> stream)
        {
            if (!StreamHasData(stream))
                return EndOfStreamError;
            Debug.Assert(stream[0] == '"');
            ReadOnlySpan<byte> cur = stream.Slice(1);
            byte c = 0;
            StringBuilder s = new StringBuilder();
            while (StreamHasData(cur) && (c = cur[0]) != '"')
            {
                if (c == '\\')
                {
                    cur = cur.Slice(1);
                    c = cur[0];
                    char escapeChar = c switch
                    {
                        (byte)'b' => '\b',
                        (byte)'f' => '\f',
                        (byte)'n' => '\n',
                        (byte)'r' => '\r',
                        (byte)'t' => '\t',
                        (byte)'"' => '"',
                        (byte)'\\' => '\\',
                        _ => char.MinValue,
                    };
                    if (escapeChar == char.MinValue)
                        return new Error($"String literal escape character '{c}' is invalid at location '{location}'");
                    s.Append(escapeChar);
                    cur = cur.Slice(1);
                }
                else if (CharIsWhitespace(c))
                    return new Error($"String literal contains invalid whitespace character '{c}' at location '{location}'");
                else
                {
                    s.Append((char)c);
                    cur = cur.Slice(1);
                }
            }
            if (c != '"')
                return new Error($"Expect string literal to end with a double quote, but got character '{c}' at location '{location}'");
            cur = cur.Slice(1);
            int len = stream.Length - cur.Length;
            JSONLocation end = location.AddColumns(len);
            return new JSONToken(JSONTokenKind.StringLiteral, location, end, s.ToString()) { Text = GetText(stream, location, end) };
        }

        static bool BufferEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i])
                    return false;
            return true;
        }

        static Result<JSONToken> GetKeywordToken(JSONLocation location, ReadOnlySpan<byte> stream, ReadOnlySpan<byte> keyword, JSONTokenKind kind)
        {
            if (!StreamHasData(stream))
                return EndOfStreamError;
            int l = Math.Min(stream.Length, keyword.Length);
            ReadOnlySpan<byte> s = stream.Slice(l);
            string expectString = GetText(keyword, keyword.Length);
            if (BufferEquals(s, keyword))
            {
                JSONLocation end = location.AddColumns(keyword.Length);
                return new JSONToken(kind, location, end, expectString);
            }
            string actual = GetText(stream, l);
            return new Error($"Expect keyword token '{expectString}', but got '{actual}' at location '{location}'");
        }

        static readonly byte[] FalseKeyword = { (byte)'f', (byte)'a', (byte)'l', (byte)'s', (byte)'e' };
        static readonly byte[] TrueKeyword = { (byte)'t', (byte)'r', (byte)'u', (byte)'e' };
        static readonly byte[] NullKeyword = { (byte)'n', (byte)'u', (byte)'l', (byte)'l' };

        static Result<JSONToken> GetToken(ref JSONLocation location, ref ReadOnlySpan<byte> stream)
        {
            if (!StreamHasData(stream))
                return EndOfStreamError;
            char c = (char)stream[0];
            switch (c)
            {
                case '{':
                    return new JSONToken(JSONTokenKind.OpenObjectOp, location, location.AddColumns(1), c);
                case '}':
                    return new JSONToken(JSONTokenKind.CloseObjectOp, location, location.AddColumns(1), c);
                case '[':
                    return new JSONToken(JSONTokenKind.OpenArrayOp, location, location.AddColumns(1), c);
                case ']':
                    return new JSONToken(JSONTokenKind.CloseArrayOp, location, location.AddColumns(1), c);
                case ':':
                    return new JSONToken(JSONTokenKind.AssignmentOp, location, location.AddColumns(1), c);
                case ',':
                    return new JSONToken(JSONTokenKind.SeparatorOp, location, location.AddColumns(1), c);

                case '"':
                    return GetStringToken(location, stream);

                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    return GetNumberToken(location, stream, false);

                case '-':
                    return GetNumberToken(location, stream, true);

                case 'f':
                    return GetKeywordToken(location, stream, FalseKeyword.AsSpan(), JSONTokenKind.FalseLiteral);
                case 't':
                    return GetKeywordToken(location, stream, TrueKeyword.AsSpan(), JSONTokenKind.TrueLiteral);
                case 'n':
                    return GetKeywordToken(location, stream, NullKeyword.AsSpan(), JSONTokenKind.NullLiteral);

                default:
                    return new Error($"Invalid character '{c}' at location '{location}'");
            }
        }

        static Result<JSONElement> ParseElement(string label, ref JSONLocation location, ref ReadOnlySpan<byte> stream)
        {
            SkipWhitespaces(ref location, ref stream);

            Result<JSONToken> tokenRes = GetToken(ref location, ref stream);
            if (!tokenRes.Success)
                return new Error($"Failed parsing token", tokenRes.Error);

            JSONToken token = tokenRes.Value;

            JSONLocation startLocation = token.Start;

            stream = stream.Slice(token.Length);

            location = token.End;

            switch (token.Kind)
            {
                case JSONTokenKind.OpenObjectOp:
                {
                    Result<JSONElement> list = ParseList(label, startLocation, ref location, ref stream, JSONElementKind.Object, JSONTokenKind.CloseObjectOp, true);
                    if (!list.Success)
                        return new Error($"Failed parsing list '{label}' at location '{token.Start}'", list.Error);
                    return list;
                }

                case JSONTokenKind.OpenArrayOp:
                {
                    Result<JSONElement> arr = ParseList(label, startLocation, ref location, ref stream, JSONElementKind.Array, JSONTokenKind.CloseArrayOp, false);
                    if (!arr.Success)
                        return new Error($"Failed parsing array '{label}' at location '{token.Start}'", arr.Error);
                    return arr;
                }

                case JSONTokenKind.NullLiteral:
                    return new JSONElement(JSONElementKind.Null, startLocation, label);

                case JSONTokenKind.StringLiteral:
                    return new JSONElement(startLocation, label, token.StringLiteral);

                case JSONTokenKind.IntegerLiteral:
                case JSONTokenKind.DecimalLiteral:
                    return new JSONElement(startLocation, label, token.NumberLiteral);

                case JSONTokenKind.FalseLiteral:
                    return new JSONElement(startLocation, label, false);
                case JSONTokenKind.TrueLiteral:
                    return new JSONElement(startLocation, label, true);

                default:
                    return new Error($"Unsupported token kind '{token.Kind}' at location '{token.Start}'");
            }

        }

        static Result<JSONElement> ParseList(string label, JSONLocation start, ref JSONLocation location, ref ReadOnlySpan<byte> stream, JSONElementKind kind, JSONTokenKind endToken, bool requireKeys)
        {
            JSONElement list = new JSONElement(kind, start, label);

            while (StreamHasData(stream))
            {
                SkipWhitespaces(ref location, ref stream);

                Result<JSONToken> tokenRes = GetToken(ref location, ref stream);
                if (!tokenRes.Success)
                    return new Error($"Failed parsing token at location '{location}'", tokenRes.Error);

                JSONToken token = tokenRes.Value;

                string childLabel;
                if (requireKeys)
                {
                    if (token.Kind != JSONTokenKind.StringLiteral)
                        return new Error($"Expect string literal token, but got '{token.Kind}' at location '{location}'");
                    stream = stream.Slice(token.Length);
                    location = token.End;
                    childLabel = token.StringLiteral;

                    SkipWhitespaces(ref location, ref stream);

                    Result<JSONToken> nextTokenRes = GetToken(ref location, ref stream);
                    if (!nextTokenRes.Success)
                        return new Error($"Failed parsing assignment operator token at location '{location}'", nextTokenRes.Error);

                    JSONToken nextToken = nextTokenRes.Value;
                    if (nextToken.Kind != JSONTokenKind.AssignmentOp)
                        return new Error($"Expect assignment token, but got '{token.Kind}' at location '{location}'");

                    stream = stream.Slice(nextToken.Length);
                    location = nextToken.End;

                    SkipWhitespaces(ref location, ref stream);
                }
                else
                    childLabel = null;

                Result<JSONElement> childRes = ParseElement(childLabel, ref location, ref stream);
                if (!childRes.Success)
                    return new Error($"Failed parsing child element '{childLabel}' at location '{location}'", childRes.Error);

                JSONElement child = childRes.Value;
                list.AddChild(child);

                SkipWhitespaces(ref location, ref stream);

                tokenRes = GetToken(ref location, ref stream);
                if (!tokenRes.Success)
                    return new Error($"Failed parsing ending/separator operator token at location '{location}'", tokenRes.Error);
                token = tokenRes.Value;

                if (token.Kind == endToken)
                {
                    stream = stream.Slice(token.Length);
                    location = token.End;
                    break;
                }
                else if (token.Kind == JSONTokenKind.SeparatorOp)
                {
                    stream = stream.Slice(token.Length);
                    location = token.End;
                }
                else
                    return new Error($"Unexpected list token '{token.Kind}' at location '{location}'");
            }

            return list;
        }

        public static Result<JSONElement> Parse(ReadOnlySpan<byte> stream)
        {
            if (!StreamHasData(stream))
                return EndOfStreamError;
            JSONLocation location = new JSONLocation();
            ReadOnlySpan<byte> cur = stream;
            Result<JSONElement> parseRes = ParseElement(null, ref location, ref cur);
            if (!parseRes.Success)
                return parseRes.Error;
            return parseRes.Value;
        }
    }
}
