using GhDucky.Utils;
using Xunit;

namespace GhDucky.Tests;

public class SqlIdentifierTests
{
    [Theory]
    [InlineData("table", "\"table\"")]
    [InlineData("My Table", "\"My Table\"")]
    [InlineData("col_1", "\"col_1\"")]
    public void Quote_WrapsInDoubleQuotes(string input, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Quote(input));
    }

    [Fact]
    public void Quote_EscapesEmbeddedDoubleQuotes()
    {
        // "foo"bar" → "foo""bar"
        Assert.Equal("\"foo\"\"bar\"", SqlIdentifier.Quote("foo\"bar"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Quote_RejectsNullOrWhitespace(string input)
    {
        Assert.Throws<System.ArgumentException>(() => SqlIdentifier.Quote(input));
    }

    [Fact]
    public void QuoteLiteral_NullReturnsNullKeyword()
    {
        Assert.Equal("NULL", SqlIdentifier.QuoteLiteral(null));
    }

    [Theory]
    [InlineData("", "''")]
    [InlineData("hello", "'hello'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("a'b'c", "'a''b''c'")]
    public void QuoteLiteral_EscapesSingleQuotes(string input, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.QuoteLiteral(input));
    }

    [Theory]
    [InlineData("",     "tbl", "\"tbl\"")]
    [InlineData(null,   "tbl", "\"tbl\"")]
    [InlineData("main", "tbl", "\"tbl\"")]
    [InlineData("MAIN", "tbl", "\"tbl\"")]
    [InlineData("Main", "tbl", "\"tbl\"")]
    public void QuoteTable_DefaultSchemaIsBare(string schema, string table, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.QuoteTable(schema, table));
    }

    [Theory]
    [InlineData("analytics", "events", "\"analytics\".\"events\"")]
    [InlineData("My Schema", "My Table", "\"My Schema\".\"My Table\"")]
    public void QuoteTable_ExplicitSchemaIsQualified(string schema, string table, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.QuoteTable(schema, table));
    }

    [Theory]
    [InlineData(null,  false)]
    [InlineData("",    false)]
    [InlineData("  ",  false)]
    [InlineData("main", false)]
    [InlineData("MAIN", false)]
    [InlineData("analytics", true)]
    public void IsExplicitSchema_OnlyTrueForNonDefault(string schema, bool expected)
    {
        Assert.Equal(expected, SqlIdentifier.IsExplicitSchema(schema));
    }
}

