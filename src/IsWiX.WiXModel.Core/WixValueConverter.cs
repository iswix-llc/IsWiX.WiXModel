using System;
using System.Globalization;

namespace IsWiX.WiXModel.Core
{
    /// <summary>Converts between attribute text and a typed literal.</summary>
    /// <typeparam name="T">The literal type.</typeparam>
    public abstract class WixValueConverter<T>
    {
        /// <summary>Parses literal text.</summary>
        /// <param name="text">The attribute text.</param>
        /// <param name="value">The parsed literal.</param>
        /// <returns><see langword="true"/> when <paramref name="text"/> is a valid literal.</returns>
        public abstract bool TryParse(string text, out T value);

        /// <summary>Formats a literal the way WiX expects it to be written.</summary>
        /// <param name="value">The literal.</param>
        /// <returns>The attribute text.</returns>
        public abstract string Format(T value);

        /// <summary>Parses attribute text into a literal, an expression, or invalid text.</summary>
        /// <param name="text">The attribute text.</param>
        /// <returns>The value; never throws for bad input.</returns>
        public WixValue<T> Parse(string text)
        {
            if (text is null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (TryParse(text, out var value))
            {
                return WixValue<T>.ParsedLiteral(value, text);
            }

            return WixExpression.ContainsExpression(text)
                ? WixValue<T>.ParsedExpression(text)
                : WixValue<T>.ParsedInvalid(text);
        }

        /// <summary>Gets the text to store for <paramref name="value"/>.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The attribute text.</returns>
        public string ToText(WixValue<T> value) => value.Kind == WixValueKind.Literal ? Format(value.Value) : value.Text!;
    }

    /// <summary>Converters for the WiX schema's built-in value types.</summary>
    public static class WixConverters
    {
        /// <summary>
        /// <c>YesNoType</c>: reads <c>yes</c>/<c>true</c> and <c>no</c>/<c>false</c>, writes <c>yes</c>/<c>no</c>.
        /// </summary>
        public static WixValueConverter<bool> YesNo { get; } = new YesNoConverter();

#pragma warning disable CA1720 // Named for the literal type, like Convert.ToInt32.

        /// <summary>32-bit integers (<c>Integer</c>, <c>LocalizableInteger</c>, <c>DiskIdType</c>, <c>xs:int</c>).</summary>
        public static WixValueConverter<int> Int32 { get; } = new Int32Converter();

        /// <summary>64-bit integers (<c>xs:long</c>).</summary>
        public static WixValueConverter<long> Int64 { get; } = new Int64Converter();
#pragma warning restore CA1720

        private sealed class YesNoConverter : WixValueConverter<bool>
        {
            public override bool TryParse(string text, out bool value)
            {
                switch (text)
                {
                    case "yes":
                    case "true":
                        value = true;
                        return true;
                    case "no":
                    case "false":
                        value = false;
                        return true;
                    default:
                        value = false;
                        return false;
                }
            }

            public override string Format(bool value) => value ? "yes" : "no";
        }

        private sealed class Int32Converter : WixValueConverter<int>
        {
            public override bool TryParse(string text, out int value) =>
                int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

            public override string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
        }

        private sealed class Int64Converter : WixValueConverter<long>
        {
            public override bool TryParse(string text, out long value) =>
                long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

            public override string Format(long value) => value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
