using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pl.Setup.Initialize
{
    /// <summary>
    /// Minimal JSON reader for the embedded seed file. Sandbox-safe: no external packages to merge.
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers long or decimal,
    /// plus string, bool and null.
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var p = new Parser(json);
            p.SkipWs();
            object v = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw p.Error("Unexpected trailing content");
            return v;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
                _i = (s.Length > 0 && s[0] == '﻿') ? 1 : 0; // tolerate a BOM
            }

            public bool End { get { return _i >= _s.Length; } }

            public FormatException Error(string msg)
            {
                return new FormatException(msg + " at position " + _i + ".");
            }

            public void SkipWs()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            public object ReadValue()
            {
                if (End) throw Error("Unexpected end of JSON");
                char c = _s[_i];
                switch (c)
                {
                    case '{': return ReadObject();
                    case '[': return ReadArray();
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private void Expect(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) throw Error("Expected '" + word + "'");
                _i += word.Length;
            }

            private Dictionary<string, object> ReadObject()
            {
                var d = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipWs();
                if (!End && _s[_i] == '}') { _i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || _s[_i] != '"') throw Error("Expected property name");
                    string key = ReadString();
                    SkipWs();
                    if (End || _s[_i] != ':') throw Error("Expected ':'");
                    _i++;
                    SkipWs();
                    d[key] = ReadValue();
                    SkipWs();
                    if (End) throw Error("Unterminated object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return d; }
                    throw Error("Expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                _i++; // [
                SkipWs();
                if (!End && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue());
                    SkipWs();
                    if (End) throw Error("Unterminated array");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("Expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Error("Unterminated string");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Error("Unterminated escape");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("Bad unicode escape");
                            sb.Append((char)int.Parse(_s.Substring(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        default: throw Error("Bad escape '\\" + e + "'");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = _i;
                if (_s[_i] == '-') _i++;
                while (_i < _s.Length && "0123456789.eE+-".IndexOf(_s[_i]) >= 0) _i++;
                string token = _s.Substring(start, _i - start);
                long l;
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 &&
                    long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                    return l;
                decimal m;
                if (decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                    return m;
                throw Error("Bad number '" + token + "'");
            }
        }
    }
}
