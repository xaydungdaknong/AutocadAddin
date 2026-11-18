using System.IO;
using DwgToDxf;
using Xunit;

public class PathUtilTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "BanVe");

    [Fact]
    public void Same_folder_puts_dxf_next_to_dwg()
    {
        var dwg = Path.Combine(Root, "Sub", "A.dwg");
        Assert.Equal(Path.Combine(Root, "Sub", "A.dxf"), PathUtil.GetOutputPath(dwg, Root, null));
        Assert.Equal(Path.Combine(Root, "Sub", "A.dxf"), PathUtil.GetOutputPath(dwg, Root, "  "));
    }

    [Fact]
    public void Other_folder_keeps_subfolder_structure()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "BanVe_DXF");
        var dwg = Path.Combine(Root, "Tang1", "KC", "Dam.DWG");
        Assert.Equal(Path.Combine(outDir, "Tang1", "KC", "Dam.dxf"), PathUtil.GetOutputPath(dwg, Root, outDir));
    }

    [Fact]
    public void Other_folder_top_level_file()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "BanVe_DXF");
        Assert.Equal(Path.Combine(outDir, "B.dxf"), PathUtil.GetOutputPath(Path.Combine(Root, "B.dwg"), Root, outDir));
    }

    [Fact]
    public void Source_root_with_trailing_separator()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "Out");
        var dwg = Path.Combine(Root, "X", "C.dwg");
        Assert.Equal(Path.Combine(outDir, "X", "C.dxf"),
            PathUtil.GetOutputPath(dwg, Root + Path.DirectorySeparatorChar, outDir));
    }

    [Fact]
    public void Relative_path_outside_root_is_empty()
    {
        Assert.Equal(string.Empty, PathUtil.GetRelativePath(Root, Path.Combine(Path.GetTempPath(), "Khac")));
        // "BanVe2" không được coi là nằm trong "BanVe".
        Assert.Equal(string.Empty, PathUtil.GetRelativePath(Root, Root + "2"));
    }

    [Theory]
    [InlineData("a.dwg", true)]
    [InlineData("A.DWG", true)]
    [InlineData("a.dwg.bak", false)]
    [InlineData("a.dwgx", false)]
    [InlineData("a.dxf", false)]
    public void IsDwg(string name, bool expected) => Assert.Equal(expected, PathUtil.IsDwg(name));

    [Theory]
    [InlineData("V2018", "2018")]
    [InlineData("V2013", "2013")]
    [InlineData("v2010", "2010")]
    [InlineData("2007", "2007")]
    [InlineData("V2004", "2004")]
    [InlineData("V2000", "2000")]
    [InlineData("R12", "R12")]
    [InlineData("r12", "R12")]
    [InlineData("", "2018")]
    [InlineData(null, "2018")]
    [InlineData("V1999", "2018")]
    public void Version_keyword(string keyword, string expected) =>
        Assert.Equal(expected, PathUtil.NormalizeVersionKeyword(keyword));

    [Fact]
    public void Normalize_folder_strips_quotes_and_spaces()
    {
        var expected = Path.GetFullPath(Root);
        Assert.Equal(expected, PathUtil.NormalizeFolder("  \"" + Root + "\"  "));
        Assert.Null(PathUtil.NormalizeFolder("   "));
        Assert.Null(PathUtil.NormalizeFolder("\"\""));
        Assert.Null(PathUtil.NormalizeFolder(null));
    }
}
