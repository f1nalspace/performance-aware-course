using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Final.PerformanceAwareCourse
{
    internal class Program
    {
        readonly struct Error
        {
            public string Message { get; }

            public Error(string message)
            {
                Message = message;
            }

            public override string ToString() => Message;
        }

        readonly struct Result<T>
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

        enum JSONTokenKind : int
        {
            None = 0,
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
            public int Position { get; }
            public int Length { get; }
            public string Text { get; internal set; }

            public double NumberLiteral { get; }
            public string StringLiteral { get; }
            public char OperatorChar { get; }


            public JSONToken(JSONTokenKind kind, int position, char opChar)
            {
                Kind = kind;
                Position = position;
                Length = 1;
                OperatorChar = opChar;
                Text = opChar.ToString();
            }

            public JSONToken(JSONTokenKind kind, int position, int length, double numberLiteral)
            {
                Kind = kind;
                Position = position;
                Length = length;
                NumberLiteral = numberLiteral;
            }

            public JSONToken(JSONTokenKind kind, int position, int length, string stringLiteral)
            {
                Kind = kind;
                Position = position;
                Length = length;
                StringLiteral = stringLiteral;
            }

            public override string ToString() => $"[Pos: {Position}, Len: {Length}, Kind: {Kind}] '{Text}'";
        }

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

        static ReadOnlySpan<byte> SkipWhitespaces(ReadOnlySpan<byte> stream)
        {
            ReadOnlySpan<byte> cur = stream;
            while (cur.Length > 0 && CharIsWhitespace(cur[0]))
                cur = cur.Slice(1);
            return cur;
        }

        static Result<JSONToken> GetNumberToken(int position, ReadOnlySpan<byte> stream, bool hasSign)
        {
            if (!StreamHasData(stream))
                return new Error("Stream is empty");

            ReadOnlySpan<byte> cur = stream;

            byte c = cur[0];

            double factor = 1.0;
            if (hasSign)
            {
                Debug.Assert(c == '-' || c == '+');
                factor = c == '-' ? -1.0 : 1.0;
                cur = cur.Slice(1);
            }
            else
                Debug.Assert(c >= '0' && c <= '9');

            if (!StreamHasData(cur))
                return new Error("Stream is empty");
            if (!CharIsNumber(c = cur[0]))
                return new Error($"Invalid number literal character '{c}' at position {position}");

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

            int len = stream.Length - cur.Length;

            double numberValue = number * factor;

            return new JSONToken(kind, position, len, numberValue) { Text = GetText(stream, len) };
        }

        static Result<JSONToken> GetStringToken(int position, ReadOnlySpan<byte> stream)
        {
            if (!StreamHasData(stream))
                return new Error("Stream is empty");
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
                        return new Error($"String literal escape character '{c}' is invalid at position {position}");
                    s.Append(escapeChar);
                    cur = cur.Slice(1);
                }
                else if (CharIsWhitespace(c))
                    return new Error($"String literal contains invalid whitespace character '{c}' at position {position}");
                else
                {
                    s.Append((char)c);
                    cur = cur.Slice(1);
                }
            }
            if (c != '"')
                return new Error($"Expect string literal to end with a double quote, but got character '{c}' at position {position}");
            cur = cur.Slice(1);
            int len = stream.Length - cur.Length;
            return new JSONToken(JSONTokenKind.StringLiteral, position, len, s.ToString()) { Text = GetText(stream, len) };
        }

        static Result<JSONToken> GetToken(int position, ReadOnlySpan<byte> stream)
        {
            if (!StreamHasData(stream))
                return new Error("Stream is empty");
            char c = (char)stream[0];
            switch (c)
            {
                case '{':
                    return new JSONToken(JSONTokenKind.OpenObjectOp, position, c);
                case '}':
                    return new JSONToken(JSONTokenKind.CloseObjectOp, position, c);
                case '[':
                    return new JSONToken(JSONTokenKind.OpenArrayOp, position, c);
                case ']':
                    return new JSONToken(JSONTokenKind.CloseArrayOp, position, c);
                case ':':
                    return new JSONToken(JSONTokenKind.AssignmentOp, position, c);
                case ',':
                    return new JSONToken(JSONTokenKind.SeparatorOp, position, c);
                case '"':
                    return GetStringToken(position, stream);
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
                    return GetNumberToken(position, stream, false);
                case '-':
                case '+':
                    return GetNumberToken(position, stream, true);
                default:
                    return new Error($"Invalid character '{c}' at position {position}");
            }
        }

        static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                string execPath = Path.GetFileName(Environment.ProcessPath);
                Console.Error.WriteLine($"Usage: {execPath} [input json file]");
                Console.Error.WriteLine($"Usage: {execPath} [input json file] [input results file]");
                return -1;
            }

            string inputJsonFilePath = args[0];
            if (!File.Exists(inputJsonFilePath))
            {
                Console.Error.WriteLine($"Input JSON file '{inputJsonFilePath}' does not exists!");
                return -1;
            }

            bool createResultsFile;
            string resultsFilePath = null;
            if (args.Length >= 2)
            {
                resultsFilePath = args[1];
                createResultsFile = false;
            }
            else
            {
                resultsFilePath = Path.ChangeExtension(inputJsonFilePath, ".results");
                createResultsFile = true;
            }

            FileInfo inputJsonFile = new FileInfo(inputJsonFilePath);

            byte[] jsonData = new byte[inputJsonFile.Length];

            const int bufferSize = 4096 * 16;

            int remainingBytes = (int)inputJsonFile.Length;
            int offset = 0;

            using (var stream = File.OpenRead(inputJsonFilePath))
            {
                while (remainingBytes > 0)
                {
                    int bytesToRead = Math.Min(remainingBytes, bufferSize);
                    int bytesRead = stream.Read(jsonData, offset, bytesToRead);
                    remainingBytes -= bytesRead;
                    offset += bytesRead;
                }
            }

            ReadOnlySpan<byte> data = jsonData.AsSpan();

            int position = 0;

            ReadOnlySpan<byte> cur = data;
            while (StreamHasData(cur))
            {
                cur = SkipWhitespaces(cur);
                if (cur.Length == 0)
                    break;
                Result<JSONToken> tokenRes = GetToken(position, cur);
                if (!tokenRes.Success)
                {
                    Console.Error.WriteLine($"Failed parsing JSON: {tokenRes.Error}");
                    return -1;
                }
                JSONToken token = tokenRes.Value;
                //Console.WriteLine(token.ToString());
                cur = cur.Slice(token.Length);
            }

            return 0;
        }
    }
}