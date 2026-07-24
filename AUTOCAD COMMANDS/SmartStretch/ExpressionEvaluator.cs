using System;
using System.Globalization;

namespace AUTOCAD_COMMANDS
{
    internal static class ExpressionEvaluator
    {
        public static double Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                throw new ArgumentException("Expression is empty.");
            }

            return new Parser(expression).Parse();
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;

            public Parser(string text)
            {
                _text = text ?? string.Empty;
            }

            public double Parse()
            {
                double result = ParseExpression();
                SkipWhiteSpace();

                if (_index != _text.Length)
                {
                    throw Error("Unexpected character '" + _text[_index] + "'.");
                }

                return EnsureFinite(result);
            }

            private double ParseExpression()
            {
                double value = ParseTerm();

                while (true)
                {
                    SkipWhiteSpace();
                    if (Match('+'))
                    {
                        value = EnsureFinite(value + ParseTerm());
                    }
                    else if (Match('-'))
                    {
                        value = EnsureFinite(value - ParseTerm());
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseTerm()
            {
                double value = ParseUnary();

                while (true)
                {
                    SkipWhiteSpace();
                    if (Match('*'))
                    {
                        value = EnsureFinite(value * ParseUnary());
                    }
                    else if (Match('/'))
                    {
                        double divisor = ParseUnary();
                        if (divisor == 0.0)
                        {
                            throw new DivideByZeroException("Cannot divide by zero.");
                        }

                        value = EnsureFinite(value / divisor);
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double ParseUnary()
            {
                SkipWhiteSpace();

                if (Match('+'))
                {
                    return ParseUnary();
                }

                if (Match('-'))
                {
                    return EnsureFinite(-ParseUnary());
                }

                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                SkipWhiteSpace();

                if (Match('('))
                {
                    double value = ParseExpression();
                    SkipWhiteSpace();

                    if (!Match(')'))
                    {
                        throw Error("Missing closing parenthesis.");
                    }

                    return value;
                }

                return ParseNumber();
            }

            private double ParseNumber()
            {
                SkipWhiteSpace();
                int start = _index;
                bool hasDigits = false;
                bool hasDecimalSeparator = false;

                while (_index < _text.Length)
                {
                    char current = _text[_index];
                    if (char.IsDigit(current))
                    {
                        hasDigits = true;
                        _index++;
                        continue;
                    }

                    if ((current == '.' || current == ',') && !hasDecimalSeparator)
                    {
                        hasDecimalSeparator = true;
                        _index++;
                        continue;
                    }

                    break;
                }

                // Keep support for scientific notation when the value is typed
                // manually or comes from a formatted double.
                if (hasDigits && _index < _text.Length &&
                    (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    int exponentStart = _index++;
                    if (_index < _text.Length &&
                        (_text[_index] == '+' || _text[_index] == '-'))
                    {
                        _index++;
                    }

                    int exponentDigitsStart = _index;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                    {
                        _index++;
                    }

                    if (exponentDigitsStart == _index)
                    {
                        _index = exponentStart;
                    }
                }

                if (!hasDigits)
                {
                    throw Error("A number was expected.");
                }

                string numberText = _text.Substring(start, _index - start).Replace(',', '.');
                if (!double.TryParse(
                    numberText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value))
                {
                    throw Error("Invalid number '" + numberText + "'.");
                }

                return EnsureFinite(value);
            }

            private bool Match(char expected)
            {
                if (_index < _text.Length && _text[_index] == expected)
                {
                    _index++;
                    return true;
                }

                return false;
            }

            private void SkipWhiteSpace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                {
                    _index++;
                }
            }

            private ArgumentException Error(string message)
            {
                return new ArgumentException(message + " Position: " + _index + ".");
            }

            private static double EnsureFinite(double value)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentException("Result is NaN or Infinity.");
                }

                return value;
            }
        }
    }
}
