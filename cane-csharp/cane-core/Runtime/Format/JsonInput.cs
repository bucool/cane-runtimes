using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Cane.Format
{
    // Used only during loading. No JSON library or reflection is required in a player.
    internal sealed class JsonInput
    {
        private readonly string text;
        private int at;
        internal JsonInput(string text) { this.text = text; }
        internal InvalidDataException Error(string message) => new InvalidDataException(message + " at JSON character " + at);
        private void Space() { while (at < text.Length && (text[at] == ' ' || text[at] == '\r' || text[at] == '\n' || text[at] == '\t')) at++; }
        internal bool Take(char token) { Space(); if (at >= text.Length || text[at] != token) return false; at++; return true; }
        internal void Expect(char token) { if (!Take(token)) throw Error("Expected " + token); }
        internal bool More(char end) { if (Take(end)) return false; Expect(','); return true; }
        internal void End() { Space(); if (at != text.Length) throw Error("Trailing JSON content"); }
        internal char Peek() { Space(); if (at == text.Length) throw Error("Truncated JSON"); return text[at]; }
        private bool Literal(string value)
        {
            Space();
            if (!text.AsSpan(at).StartsWith(value.AsSpan(), StringComparison.Ordinal)) return false;
            at += value.Length;
            return true;
        }
        internal bool Present() => !Literal("null");
        internal bool Boolean()
        {
            if (Literal("true")) return true;
            if (Literal("false")) return false;
            throw Error("Expected boolean");
        }
        internal string String()
        {
            Expect('"');
            int start = at;
            StringBuilder? escaped = null;
            while (at < text.Length)
            {
                char c = text[at++];
                if (c == '"')
                {
                    if (escaped == null) return text.Substring(start, at - start - 1);
                    escaped.Append(text, start, at - start - 1);
                    return escaped.ToString();
                }
                if (c < 32) throw Error("Control character in string");
                if (c != '\\') continue;
                escaped ??= new StringBuilder();
                escaped.Append(text, start, at - start - 1);
                if (at == text.Length) throw Error("Truncated escape");
                c = text[at++];
                switch (c)
                {
                    case '"': case '\\': case '/': escaped.Append(c); break;
                    case 'b': escaped.Append('\b'); break;
                    case 'f': escaped.Append('\f'); break;
                    case 'n': escaped.Append('\n'); break;
                    case 'r': escaped.Append('\r'); break;
                    case 't': escaped.Append('\t'); break;
                    case 'u':
                        if (at + 4 > text.Length || !ushort.TryParse(text.AsSpan(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                            throw Error("Invalid Unicode escape");
                        escaped.Append((char)code); at += 4; break;
                    default: throw Error("Invalid escape");
                }
                start = at;
            }
            throw Error("Unterminated string");
        }
        internal double Number()
        {
            Space(); int start = at;
            if (at < text.Length && text[at] == '-') at++;
            if (at >= text.Length) throw Error("Truncated number");
            if (text[at] == '0') at++;
            else { if (text[at] < '1' || text[at] > '9') throw Error("Expected number"); Digits(); }
            if (at < text.Length && text[at] == '.') { at++; Digits(); }
            if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
            {
                at++;
                if (at < text.Length && (text[at] == '+' || text[at] == '-')) at++;
                Digits();
            }
            if (!double.TryParse(text.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || double.IsInfinity(value)) throw Error("Invalid numeric value");
            return value == 0 && text[start] == '-' ? BitConverter.Int64BitsToDouble(long.MinValue) : value;
        }
        private void Digits()
        {
            int start = at;
            while (at < text.Length && text[at] >= '0' && text[at] <= '9') at++;
            if (at == start) throw Error("Expected digits");
        }
        internal uint UInt()
        {
            double value = Number();
            if (value < 0 || value > uint.MaxValue || value != Math.Truncate(value)) throw Error("Expected unsigned integer");
            return (uint)value;
        }
        internal long Integer()
        {
            double value = Number();
            if (Math.Abs(value) > 9007199254740991 || value != Math.Truncate(value)) throw Error("Expected exact integer");
            return (long)value;
        }
        internal T[] List<T>(Func<JsonInput, T> read)
        {
            Expect('['); if (Take(']')) return Array.Empty<T>();
            var values = new List<T>();
            do { values.Add(read(this)); } while (More(']'));
            return values.ToArray();
        }
        internal T[] Tuple<T>(int length, Func<JsonInput, T> read)
        {
            Expect('['); var values = new T[length];
            for (int i = 0; i < length; i++) { if (i != 0) Expect(','); values[i] = read(this); }
            Expect(']'); return values;
        }
        // The tag may occur after other fields. Scan without allocating a temporary object.
        internal string Discriminator()
        {
            int saved = at;
            Expect('{');
            if (!Take('}')) do
            {
                string field = String(); Expect(':');
                if (field == "type") { string tag = String(); at = saved; return tag; }
                Skip(0);
            } while (More('}'));
            throw Error("Missing record type");
        }
        private void Skip(int depth)
        {
            if (depth > 64) throw Error("JSON nesting exceeds the runtime format");
            switch (Peek())
            {
                case '{': Expect('{'); if (!Take('}')) do { String(); Expect(':'); Skip(depth + 1); } while (More('}')); break;
                case '[': Expect('['); if (!Take(']')) do { Skip(depth + 1); } while (More(']')); break;
                case '"': String(); break;
                case 't': case 'f': Boolean(); break;
                case 'n': if (!Literal("null")) throw Error("Invalid literal"); break;
                default: Number(); break;
            }
        }
    }
}
