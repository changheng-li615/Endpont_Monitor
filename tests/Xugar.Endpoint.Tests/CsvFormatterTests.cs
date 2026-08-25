using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class CsvFormatterTests
{
    [Fact]
    public void EscapeUsesRfcCompatibleQuoting()
    {
        Assert.Equal(string.Empty, CsvFormatter.Escape(null));
        Assert.Equal("plain", CsvFormatter.Escape("plain"));
        Assert.Equal("\"comma,value\"", CsvFormatter.Escape("comma,value"));
        Assert.Equal("\"quoted \"\"value\"\"\"", CsvFormatter.Escape("quoted \"value\""));
        Assert.Equal("\"line1\r\nline2\"", CsvFormatter.Escape("line1\r\nline2"));
    }

    [Fact]
    public void EscapedRowRoundTripsThroughParser()
    {
        var csv = CsvFormatter.CreateRow("name,with,commas", "quoted \"value\"", "two\nlines", null);

        var rows = CsvFormatter.ParseRows(csv);

        var row = Assert.Single(rows);
        Assert.Equal(["name,with,commas", "quoted \"value\"", "two\nlines", string.Empty], row);
    }
}
