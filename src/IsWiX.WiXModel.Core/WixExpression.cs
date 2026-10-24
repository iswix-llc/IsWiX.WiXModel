using System.Text.RegularExpressions;

namespace IsWiX.WiXModel.Core
{
    /// <summary>Recognizes WiX build-time expressions in attribute text.</summary>
    public static class WixExpression
    {
        // $(var.Name), $(env.Name), $(sys.Name), $(Name) and the function form $(fun.Name(args));
        // !(loc.Name), !(bind.Name), !(wix.Name).
        private static readonly Regex Pattern = new Regex(
            @"\$\([A-Za-z_][\w.]*(\([^)]*\))?\)|!\((loc|bind|wix)\.[^)]+\)",
            RegexOptions.CultureInvariant);

        /// <summary>Determines whether <paramref name="text"/> contains a preprocessor, localization or bind expression.</summary>
        /// <param name="text">The attribute text.</param>
        /// <returns><see langword="true"/> when an expression is present.</returns>
        public static bool ContainsExpression(string? text) => text != null && Pattern.IsMatch(text);
    }
}
