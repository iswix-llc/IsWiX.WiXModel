using System;
using System.Collections.Generic;

namespace IsWiX.WiXModel.Core
{
    /// <summary>Describes what a <see cref="WixValue{T}"/> holds.</summary>
    public enum WixValueKind
    {
        /// <summary>A value of the attribute's type, such as <c>yes</c> or <c>42</c>.</summary>
        Literal,

        /// <summary>
        /// A preprocessor, localization or bind expression (<c>$(var.X)</c>, <c>!(loc.X)</c>, <c>!(bind.X)</c>)
        /// that WiX resolves at build time.
        /// </summary>
        Expression,

        /// <summary>Text that is neither a valid literal nor an expression. It is kept as written.</summary>
        Invalid,
    }

    /// <summary>
    /// The value of a typed WiX attribute. WiX lets most typed attributes hold a build-time expression
    /// instead of a literal, so a value is either a typed literal, an expression, or text that is not valid
    /// for the attribute (kept so that reading a file never loses or rejects what the author wrote).
    /// </summary>
    /// <typeparam name="T">The literal type, such as <see cref="bool"/>, <see cref="int"/> or a schema enum.</typeparam>
    /// <example>
    /// <code>
    /// component.KeyPath = true;                                        // literal
    /// component.KeyPath = WixValue&lt;bool&gt;.FromExpression("$(var.KeyPath)"); // expression
    /// if (component.KeyPath is { Kind: WixValueKind.Literal } keyPath &amp;&amp; keyPath.Value) { ... }
    /// </code>
    /// </example>
    public readonly struct WixValue<T> : IEquatable<WixValue<T>>
    {
        private readonly T _value;
        private readonly string? _text;

        private WixValue(WixValueKind kind, T value, string? text)
        {
            Kind = kind;
            _value = value;
            _text = text;
        }

        /// <summary>Gets what this value holds.</summary>
        public WixValueKind Kind { get; }

        /// <summary>Gets a value indicating whether this is a <see cref="WixValueKind.Literal"/>.</summary>
        public bool IsLiteral => Kind == WixValueKind.Literal;

        /// <summary>Gets the literal value.</summary>
        /// <exception cref="InvalidOperationException">The value is an expression or invalid text.</exception>
        public T Value => Kind == WixValueKind.Literal
            ? _value
            : throw new InvalidOperationException($"'{_text}' is {(Kind == WixValueKind.Expression ? "an expression" : "not a valid value")}, not a literal. Check Kind or use TryGetValue.");

        /// <summary>
        /// Gets the text as written in the source, or <see langword="null"/> for a literal created in code
        /// (its text comes from the attribute's converter when it is stored).
        /// </summary>
        public string? Text => _text;

        /// <summary>Creates a literal value.</summary>
        /// <param name="value">The literal.</param>
        /// <returns>The value.</returns>
#pragma warning disable CA1000 // Static factories on the generic type keep call sites short, as with ImmutableArray<T>.
        public static WixValue<T> FromLiteral(T value) => new WixValue<T>(WixValueKind.Literal, value, null);

        /// <summary>Creates an expression value such as <c>$(var.Version)</c> or <c>!(loc.ProductName)</c>.</summary>
        /// <param name="expression">The expression text.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentException"><paramref name="expression"/> contains no WiX expression.</exception>
        public static WixValue<T> FromExpression(string expression)
        {
            if (!WixExpression.ContainsExpression(expression))
            {
                throw new ArgumentException($"'{expression}' does not contain a $(...) or !(...) expression.", nameof(expression));
            }

            return new WixValue<T>(WixValueKind.Expression, default!, expression);
        }
#pragma warning restore CA1000

        internal static WixValue<T> ParsedLiteral(T value, string text) => new WixValue<T>(WixValueKind.Literal, value, text);

        internal static WixValue<T> ParsedExpression(string text) => new WixValue<T>(WixValueKind.Expression, default!, text);

        internal static WixValue<T> ParsedInvalid(string text) => new WixValue<T>(WixValueKind.Invalid, default!, text);

        /// <summary>Gets the literal value if there is one.</summary>
        /// <param name="value">The literal, or the default of <typeparamref name="T"/>.</param>
        /// <returns><see langword="true"/> when this is a literal.</returns>
        public bool TryGetValue(out T value)
        {
            value = _value;
            return Kind == WixValueKind.Literal;
        }

        /// <summary>Converts a literal to a <see cref="WixValue{T}"/>.</summary>
        /// <param name="value">The literal.</param>
#pragma warning disable CA2225 // FromLiteral is the named alternative.
        public static implicit operator WixValue<T>(T value) => FromLiteral(value);
#pragma warning restore CA2225

        /// <inheritdoc/>
        public bool Equals(WixValue<T> other) =>
            Kind == other.Kind &&
            (Kind == WixValueKind.Literal
                ? EqualityComparer<T>.Default.Equals(_value, other._value)
                : string.Equals(_text, other._text, StringComparison.Ordinal));

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is WixValue<T> other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => Kind == WixValueKind.Literal
            ? EqualityComparer<T>.Default.GetHashCode(_value!)
            : StringComparer.Ordinal.GetHashCode(_text!);

        /// <summary>Compares two values.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><see langword="true"/> when equal.</returns>
        public static bool operator ==(WixValue<T> left, WixValue<T> right) => left.Equals(right);

        /// <summary>Compares two values.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        /// <returns><see langword="true"/> when not equal.</returns>
        public static bool operator !=(WixValue<T> left, WixValue<T> right) => !left.Equals(right);

        /// <inheritdoc/>
        public override string ToString() => _text ?? _value?.ToString() ?? string.Empty;
    }
}
