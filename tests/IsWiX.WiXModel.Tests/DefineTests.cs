using System;
using System.Linq;
using IsWiX.WiXModel.Core;
using Xunit;
using static IsWiX.WiXModel.Tests.TestText;

namespace IsWiX.WiXModel.Tests;

/// <summary>Reading and editing &lt;?define?&gt; instructions through <see cref="WixSourceDocument.Defines"/>.</summary>
public class DefineTests
{
    private const string Source = """
        <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
          <?define ComponentRules="OneToOne"?>
          <!-- where the files come from -->
          <?define SourceDir = "..\Deploy" ?>
          <?define Bare=1?>
          <?define Flag?>
          <?if $(sys.BUILDARCH) = x64 ?>
          <?define Arch = "x64" ?>
          <?else?>
          <?define Arch = "x86" ?>
          <?endif?>
          <Fragment>
            <ComponentGroup Id="Files" />
          </Fragment>
        </Wix>
        """;

    [Fact]
    public void Defines_are_read_with_their_values_and_whether_they_are_conditional()
    {
        var document = Parse(Source);

        Assert.Equal(
            ["ComponentRules=OneToOne", "SourceDir=..\\Deploy", "Bare=1", "Flag=", "Arch=x64?", "Arch=x86?"],
            document.Defines.Select(d => $"{d.Name}={d.Value}{(d.IsConditional ? "?" : "")}"));
        Assert.Equal("..\\Deploy", document.Defines["SourceDir"]);
        Assert.Null(document.Defines["Flag"]);
        Assert.Equal("x64", document.Defines["Arch"]);
        Assert.Null(document.Defines["Missing"]);
        Assert.Equal(Lf(Source), document.ToString());
    }

    [Fact]
    public void Changing_a_value_keeps_the_define_s_spacing_and_quotes()
    {
        var document = Parse(Source);

        document.Defines["SourceDir"] = @"..\Output";
        document.Defines["ComponentRules"] = "OneToMany";
        document.Defines["Bare"] = "2";
        document.Defines.Set("Flag", "on");

        Assert.Equal(
            Lf(Source)
                .Replace("SourceDir = \"..\\Deploy\" ?>", "SourceDir = \"..\\Output\" ?>", StringComparison.Ordinal)
                .Replace("ComponentRules=\"OneToOne\"", "ComponentRules=\"OneToMany\"", StringComparison.Ordinal)
                .Replace("<?define Bare=1?>", "<?define Bare=2?>", StringComparison.Ordinal)
                .Replace("<?define Flag?>", "<?define Flag=\"on\"?>", StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void A_bare_value_that_needs_quotes_is_quoted()
    {
        var document = Parse(Source);

        var define = document.Defines.Set("Bare", "two words");

        Assert.Equal("two words", define.Value);
        Assert.Contains("<?define Bare=\"two words\"?>", document.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_define_goes_after_the_others_on_its_own_line()
    {
        var document = Parse(Source);

        var define = document.Defines.Set("Version", "1.0.0");

        // After the last unconditional define, not inside the <?if?> block.
        Assert.False(define.IsConditional);
        Assert.Equal("1.0.0", document.Defines["Version"]);
        Assert.Equal(
            Lf(Source).Replace("  <?define Flag?>\n", "  <?define Flag?>\n  <?define Version=\"1.0.0\"?>\n", StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void The_first_define_goes_first_in_the_root()
    {
        var source = """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">

              <Fragment />
            </Wix>
            """;
        var document = Parse(source);

        document.Defines["SourceDir"] = ".";

        Assert.Equal(
            Lf(source).Replace(
                "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n",
                "<Wix xmlns=\"http://wixtoolset.org/schemas/v4/wxs\">\n  <?define SourceDir=\".\"?>\n",
                StringComparison.Ordinal),
            document.ToString());
    }

    [Fact]
    public void Removing_a_define_removes_its_line()
    {
        var document = Parse(Source);

        Assert.True(document.Defines.Remove("Bare"));
        Assert.False(document.Defines.Remove("Bare"));

        Assert.Equal(Lf(Source).Replace("  <?define Bare=1?>\n", "", StringComparison.Ordinal), document.ToString());
    }

    [Fact]
    public void A_name_defined_only_in_conditional_blocks_is_not_guessed_at()
    {
        var document = Parse(Source);

        Assert.Throws<InvalidOperationException>(() => document.Defines.Set("Arch", "arm64"));
        Assert.Equal(Lf(Source), document.ToString());
    }

    [Fact]
    public void Values_and_names_are_checked()
    {
        var document = Parse(Source);

        Assert.Throws<ArgumentException>(() => document.Defines.Set("New", "say \"hi\""));
        Assert.Throws<ArgumentException>(() => document.Defines.Set("SourceDir", "a\"b"));
        Assert.Throws<ArgumentException>(() => document.Defines.Set("1st", "x"));
        Assert.Equal(Lf(Source), document.ToString());
    }

    [Fact]
    public void Include_files_have_defines_too()
    {
        var include = WixIncludeDocument.Parse(Lf("""
            <Include xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <?define Product = "Acme" ?>
            </Include>
            """));

        include.Defines["Product"] = "Acme Tools";
        include.Defines["Company"] = "Acme";

        Assert.Equal(Lf("""
            <Include xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <?define Product = "Acme Tools" ?>
              <?define Company="Acme"?>
            </Include>
            """), include.ToString());
    }
}
