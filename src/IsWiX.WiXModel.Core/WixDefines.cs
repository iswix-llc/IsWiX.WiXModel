using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace IsWiX.WiXModel.Core
{
    /// <summary>
    /// One <c>&lt;?define Name = "Value"?&gt;</c> preprocessor instruction. Reading never changes the instruction;
    /// setting <see cref="Value"/> rewrites only the value, keeping the instruction's spacing and quote style.
    /// </summary>
    public sealed class WixDefine
    {
        private int _valueStart;
        private char? _quote;

        internal WixDefine(XProcessingInstruction instruction, bool isConditional)
        {
            Instruction = instruction;
            IsConditional = isConditional;
            Name = Parse();
        }

        // Finds where the value starts and how it is quoted; returns the name.
        private string Parse()
        {
            var data = Instruction.Data;
            var equals = data.IndexOf('=');
            _quote = null;
            if (equals < 0)
            {
                _valueStart = -1;
                return data.Trim();
            }

            var start = equals + 1;
            while (start < data.Length && char.IsWhiteSpace(data[start]))
            {
                start++;
            }

            _valueStart = start;
            if (start < data.Length && (data[start] == '"' || data[start] == '\''))
            {
                _quote = data[start];
            }

            return data.Substring(0, equals).Trim();
        }

        /// <summary>Gets the instruction.</summary>
        public XProcessingInstruction Instruction { get; }

        /// <summary>Gets the variable name, used as <c>$(var.Name)</c>.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets a value indicating whether the define is inside a <c>&lt;?if?&gt;</c>, <c>&lt;?ifdef?&gt;</c>,
        /// <c>&lt;?ifndef?&gt;</c> or <c>&lt;?foreach?&gt;</c> block, so it may not apply to every build.
        /// </summary>
        public bool IsConditional { get; }

        /// <summary>
        /// Gets or sets the value, without its quotes; <see langword="null"/> for <c>&lt;?define Name?&gt;</c>.
        /// </summary>
        /// <exception cref="ArgumentException">The value contains the quote character the define is written with.</exception>
        public string? Value
        {
            get
            {
                if (_valueStart < 0)
                {
                    return null;
                }

                var data = Instruction.Data;
                if (_quote is char quote)
                {
                    var end = data.IndexOf(quote, _valueStart + 1);
                    return end < 0 ? data.Substring(_valueStart + 1) : data.Substring(_valueStart + 1, end - _valueStart - 1);
                }

                return data.Substring(_valueStart).TrimEnd();
            }

            set
            {
                if (value is null)
                {
                    throw new ArgumentNullException(nameof(value), "Remove the define instead of clearing its value.");
                }

                if (value == Value)
                {
                    return;
                }

                // Written quoted unless it already was a bare value that stays bare.
                var quote = _quote ?? (_valueStart >= 0 && IsBare(value) ? (char?)null : '"');
                if (quote is char q && value.IndexOf(q) >= 0)
                {
                    throw new ArgumentException($"A define written with {q} quotes can't contain {q}.", nameof(value));
                }

                var prefix = _valueStart < 0 ? Name + "=" : Instruction.Data.Substring(0, _valueStart);
                var text = quote is char c ? c + value + c : value;
                Instruction.Data = prefix + text + Trailing();
                Parse();
            }
        }

        /// <summary>Removes the instruction, and the line it was on.</summary>
        public void Remove()
        {
            if (Instruction.PreviousNode is XText previous && WixDefines.IsWhitespace(previous) && previous.Value.IndexOf('\n') >= 0)
            {
                previous.Remove();
            }

            Instruction.Remove();
        }

        /// <inheritdoc/>
        public override string ToString() => $"<?define {Instruction.Data}?>";

        // Whatever followed the value (usually a space before ?>), kept as written.
        private string Trailing()
        {
            if (_valueStart < 0)
            {
                return string.Empty;
            }

            var data = Instruction.Data;
            if (_quote is char quote)
            {
                var end = data.IndexOf(quote, _valueStart + 1);
                return end < 0 ? string.Empty : data.Substring(end + 1);
            }

            return data.Substring(data.TrimEnd().Length);
        }

        private static bool IsBare(string value) => value.Length > 0 && value.All(c => !char.IsWhiteSpace(c) && c != '"' && c != '\'');
    }

    /// <summary>
    /// The <c>&lt;?define?&gt;</c> preprocessor instructions of a WiX source or include document, in document
    /// order. The view is live: it reads the document each time.
    /// </summary>
    public sealed class WixDefines : IEnumerable<WixDefine>
    {
        private static readonly HashSet<string> Opens = new HashSet<string>(StringComparer.Ordinal) { "if", "ifdef", "ifndef", "foreach" };
        private static readonly HashSet<string> Closes = new HashSet<string>(StringComparer.Ordinal) { "endif", "endforeach" };

        private readonly XDocument _document;

        /// <summary>Creates a view of a document's defines.</summary>
        /// <param name="document">The document.</param>
        public WixDefines(XDocument document) => _document = document ?? throw new ArgumentNullException(nameof(document));

        /// <summary>
        /// Gets a define's value: the unconditional define's when there is one, else the first conditional one's;
        /// <see langword="null"/> when the name is not defined. Setting adds or changes it, as <see cref="Set"/>.
        /// </summary>
        /// <param name="name">The variable name.</param>
        public string? this[string name]
        {
            get => Find(name)?.Value;
            set
            {
                if (value is null)
                {
                    Remove(name);
                }
                else
                {
                    Set(name, value);
                }
            }
        }

        /// <summary>
        /// Finds a define: the unconditional one when there is one, else the first conditional one.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <returns>The define, or <see langword="null"/>.</returns>
        public WixDefine? Find(string name)
        {
            var matches = this.Where(d => d.Name == name).ToList();
            return matches.FirstOrDefault(d => !d.IsConditional) ?? matches.FirstOrDefault();
        }

        /// <summary>
        /// Sets a define's value. An existing unconditional define is changed in place; otherwise a new one is
        /// added after the root element's last define, or first in the root element, on its own line.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The value, written in double quotes for a new define.</param>
        /// <returns>The define.</returns>
        /// <exception cref="InvalidOperationException">
        /// The name is only defined inside conditional blocks, so which one to change is ambiguous.
        /// </exception>
        public WixDefine Set(string name, string value)
        {
            ValidateName(name);
            if (value is null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var existing = this.Where(d => d.Name == name).ToList();
            var unconditional = existing.FirstOrDefault(d => !d.IsConditional);
            if (unconditional != null)
            {
                unconditional.Value = value;
                return unconditional;
            }

            if (existing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"'{name}' is only defined inside <?if?> or <?foreach?> blocks; change those defines directly.");
            }

            if (value.IndexOf('"') >= 0)
            {
                throw new ArgumentException("A new define is written in double quotes and can't contain them.", nameof(value));
            }

            var root = _document.Root ?? throw new InvalidOperationException("The document has no root element.");
            var instruction = new XProcessingInstruction("define", $"{name}=\"{value}\"");
            // After the root's last unconditional define, so defines stay together and outside <?if?> blocks.
            var after = this.LastOrDefault(d => !d.IsConditional && d.Instruction.Parent == root)?.Instruction;
            var whitespace = root.FirstNode as XText;
            if (after != null && after.PreviousNode is XText line && IsWhitespace(line))
            {
                after.AddAfterSelf(new XText(LastLine(line)), instruction);
            }
            else if (whitespace != null && IsWhitespace(whitespace))
            {
                // Right under the root's start tag; any blank line stays between the defines and what follows.
                root.AddFirst(new XText(LastLine(whitespace)), instruction);
            }
            else
            {
                root.AddFirst(instruction);
            }

            return new WixDefine(instruction, isConditional: false);
        }

        /// <summary>Removes every define of a name, conditional or not.</summary>
        /// <param name="name">The variable name.</param>
        /// <returns><see langword="true"/> when one was removed.</returns>
        public bool Remove(string name)
        {
            var matches = this.Where(d => d.Name == name).ToList();
            matches.ForEach(d => d.Remove());
            return matches.Count > 0;
        }

        /// <inheritdoc/>
        public IEnumerator<WixDefine> GetEnumerator()
        {
            var depth = 0;
            foreach (var instruction in _document.DescendantNodes().OfType<XProcessingInstruction>().ToList())
            {
                if (Opens.Contains(instruction.Target))
                {
                    depth++;
                }
                else if (Closes.Contains(instruction.Target))
                {
                    depth = Math.Max(0, depth - 1);
                }
                else if (instruction.Target == "define")
                {
                    yield return new WixDefine(instruction, depth > 0);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        // A line break and the indentation after it, without any blank lines before.
        private static string LastLine(XText whitespace)
        {
            var value = whitespace.Value;
            var newline = value.LastIndexOf('\n');
            return newline < 0 ? value : value.Remove(0, newline);
        }

        internal static bool IsWhitespace(XText text) => text.Value.Trim(' ', '\t', '\r', '\n').Length == 0;

        private static void ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name[0]) || name[0] == '_') || !name.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.'))
            {
                throw new ArgumentException($"'{name}' is not a valid preprocessor variable name.", nameof(name));
            }
        }
    }
}
