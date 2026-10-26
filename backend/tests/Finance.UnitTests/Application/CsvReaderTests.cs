using Finance.Application.Imports;
using Finance.Domain.Common;

namespace Finance.UnitTests.Application;

public sealed class CsvReaderTests
{
    [Fact]
    public void Csv_SupportsBomEscapesQuotedDelimitersAndMultilineFields()
    {
        var rows = CsvReader.Read("\uFEFFDate;Amount;Description\r\n2026-10-25;25,01;\"Чипсы; \"\"сыр\"\"\r\nещё\"\r\n", ';');
        Assert.Equal(new[] { "Date", "Amount", "Description" }, rows[0]);
        Assert.Equal(new[] { "2026-10-25", "25,01", "Чипсы; \"сыр\"\r\nещё" }, rows[1]);
        Assert.Equal(2, rows.Count);
        Assert.Equal("", CsvReader.Read("A,B\n1,", ',')[1][1]);
    }

    [Theory]
    [InlineData("A;B\n1;\"unclosed")]
    [InlineData("A;B\n1;\"closed\"junk")]
    [InlineData("A;B\n1;un\"quoted")]
    [InlineData("A;B")]
    public void Csv_RejectsMalformedInput(string content) => Assert.Throws<DomainException>(() => CsvReader.Read(content, ';'));

    [Fact]
    public void Csv_BoundsWorkBeforeDatabaseAccess()
    {
        Assert.Throws<DomainException>(() => CsvReader.Read(new string('a', CsvReader.MaxCharacters + 1), ';'));
        Assert.Throws<DomainException>(() => CsvReader.Read("A\n" + string.Concat(Enumerable.Repeat("1\n", CsvReader.MaxRows + 1)), ';'));
        Assert.Throws<DomainException>(() => CsvReader.Read("A\n1", '|'));
        Assert.Throws<DomainException>(() => CsvReader.Read(string.Join(';', Enumerable.Repeat("A", 51)) + "\n1", ';'));
    }
}
