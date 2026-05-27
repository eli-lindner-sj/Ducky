using System;
using GhDucky.Utils;
using Xunit;

namespace GhDucky.Tests;

/// <summary>
/// Covers the pure-CLR portions of <see cref="TypeMapping"/> and the
/// <see cref="DuckyColumnType"/> parser/formatter. Methods that produce or
/// consume Grasshopper <c>IGH_Goo</c> values (<c>ToGoo</c>, <c>Unwrap</c>) are
/// exercised indirectly elsewhere; instantiating them here would require a
/// live Grasshopper / Rhino runtime.
/// </summary>
public class TypeMappingTests
{
    // ---------- InferColumnType ----------

    [Fact]
    public void InferColumnType_AllNullSamples_DefaultsToVarchar()
    {
        Assert.Equal(DuckyColumnType.Varchar,
            TypeMapping.InferColumnType(new object[] { null, null, null }));
    }

    [Fact]
    public void InferColumnType_EmptySamples_DefaultsToVarchar()
    {
        Assert.Equal(DuckyColumnType.Varchar,
            TypeMapping.InferColumnType(Array.Empty<object>()));
    }

    [Theory]
    [InlineData(typeof(bool),     DuckyColumnType.Boolean)]
    [InlineData(typeof(int),      DuckyColumnType.Integer)]
    [InlineData(typeof(long),     DuckyColumnType.BigInt)]
    [InlineData(typeof(double),   DuckyColumnType.Double)]
    [InlineData(typeof(string),   DuckyColumnType.Varchar)]
    public void InferColumnType_SingleType(Type clrType, DuckyColumnType expected)
    {
        var sample = GetSample(clrType);
        Assert.Equal(expected, TypeMapping.InferColumnType(new[] { sample }));
    }

    [Fact]
    public void InferColumnType_PromotesIntToBigInt()
    {
        Assert.Equal(DuckyColumnType.BigInt,
            TypeMapping.InferColumnType(new object[] { 1, 2L, 3 }));
    }

    [Fact]
    public void InferColumnType_PromotesIntegerToDoubleWhenMixed()
    {
        Assert.Equal(DuckyColumnType.Double,
            TypeMapping.InferColumnType(new object[] { 1, 2.5, 3 }));
    }

    [Fact]
    public void InferColumnType_TimestampWinsOverDouble()
    {
        Assert.Equal(DuckyColumnType.Timestamp,
            TypeMapping.InferColumnType(new object[] { 1.5, DateTime.UtcNow }));
    }

    [Fact]
    public void InferColumnType_VarcharIsWidest()
    {
        Assert.Equal(DuckyColumnType.Varchar,
            TypeMapping.InferColumnType(new object[] { 1, "hello", true }));
    }

    [Fact]
    public void InferColumnType_IgnoresNulls()
    {
        Assert.Equal(DuckyColumnType.Integer,
            TypeMapping.InferColumnType(new object[] { null, 1, null, 2 }));
    }

    // ---------- CoerceForAppender ----------

    [Fact]
    public void Coerce_NullReturnsNull()
    {
        Assert.Null(TypeMapping.CoerceForAppender(null, DuckyColumnType.Integer));
    }

    [Theory]
    [InlineData("true",  true)]
    [InlineData("false", false)]
    public void Coerce_BooleanFromString(string input, bool expected)
    {
        Assert.Equal(expected, TypeMapping.CoerceForAppender(input, DuckyColumnType.Boolean));
    }

    [Fact]
    public void Coerce_BooleanFromBoolIsIdentity()
    {
        Assert.Equal(true, TypeMapping.CoerceForAppender(true, DuckyColumnType.Boolean));
    }

    [Theory]
    [InlineData((short)5, 5)]
    [InlineData(5L,        5)]
    [InlineData(5.0,       5)]
    [InlineData("42",      42)]
    public void Coerce_IntegerWidens(object input, int expected)
    {
        Assert.Equal(expected, TypeMapping.CoerceForAppender(input, DuckyColumnType.Integer));
    }

    [Fact]
    public void Coerce_BigIntFromInt()
    {
        Assert.Equal(123L, TypeMapping.CoerceForAppender(123, DuckyColumnType.BigInt));
    }

    [Fact]
    public void Coerce_DoubleFromIntegerString()
    {
        Assert.Equal(7.0, TypeMapping.CoerceForAppender("7", DuckyColumnType.Double));
    }

    [Fact]
    public void Coerce_TimestampFromString()
    {
        var coerced = TypeMapping.CoerceForAppender("2024-05-01T12:34:56", DuckyColumnType.Timestamp);
        var dt = Assert.IsType<DateTime>(coerced);
        Assert.Equal(new DateTime(2024, 5, 1, 12, 34, 56), dt);
    }

    [Fact]
    public void Coerce_TimestampFromDateTime_Identity()
    {
        var now = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Assert.Equal(now, TypeMapping.CoerceForAppender(now, DuckyColumnType.Timestamp));
    }

    [Fact]
    public void Coerce_TimestampFromDateTimeOffset_ReturnsUtc()
    {
        var dto = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(5));
        var coerced = TypeMapping.CoerceForAppender(dto, DuckyColumnType.Timestamp);
        Assert.Equal(dto.UtcDateTime, coerced);
    }

    [Theory]
    [InlineData(42,    "42")]
    [InlineData(true,  "True")]
    [InlineData("hi",  "hi")]
    public void Coerce_VarcharStringifies(object input, string expected)
    {
        Assert.Equal(expected, TypeMapping.CoerceForAppender(input, DuckyColumnType.Varchar));
    }

    [Fact]
    public void Coerce_FailedConversion_FallsBackToString()
    {
        // "not a number" cannot be parsed to int → fallback is the stringified value.
        Assert.Equal("not a number",
            TypeMapping.CoerceForAppender("not a number", DuckyColumnType.Integer));
    }

    // ---------- ResolveColumnNames ----------

    [Fact]
    public void ResolveColumnNames_PadsWithDefaults()
    {
        var result = TypeMapping.ResolveColumnNames(new[] { "a", "b" }, 4);
        Assert.Equal(new[] { "a", "b", "col_2", "col_3" }, result);
    }

    [Fact]
    public void ResolveColumnNames_NullSupplied_AllDefault()
    {
        var result = TypeMapping.ResolveColumnNames(null, 3);
        Assert.Equal(new[] { "col_0", "col_1", "col_2" }, result);
    }

    [Fact]
    public void ResolveColumnNames_TrimsAndReplacesEmpties()
    {
        var result = TypeMapping.ResolveColumnNames(new[] { "  alpha  ", "", "gamma" }, 3);
        Assert.Equal(new[] { "alpha", "col_1", "gamma" }, result);
    }

    // ---------- ResolveColumnTypes ----------

    [Fact]
    public void ResolveColumnTypes_AutoInferenceFillsMissing()
    {
        var columns = new[]
        {
            new object[] { 1, 2, 3 },        // INTEGER
            new object[] { 1.0, 2.0 },       // DOUBLE
            new object[] { "x", "y" },       // VARCHAR
        };
        var result = TypeMapping.ResolveColumnTypes(null, columns);
        Assert.Equal(
            new[] { DuckyColumnType.Integer, DuckyColumnType.Double, DuckyColumnType.Varchar },
            result);
    }

    [Fact]
    public void ResolveColumnTypes_SuppliedNamesAreParsed()
    {
        var columns = new[]
        {
            new object[] { 1, 2 },
            new object[] { 3, 4 },
        };
        var result = TypeMapping.ResolveColumnTypes(new[] { "  bigint  ", "integer" }, columns);
        Assert.Equal(new[] { DuckyColumnType.BigInt, DuckyColumnType.Integer }, result);
    }

    [Fact]
    public void ResolveColumnTypes_PartialSupplyFallsBackToInference()
    {
        var columns = new[]
        {
            new object[] { 1, 2 },           // would infer INTEGER
            new object[] { 1.5, 2.5 },       // would infer DOUBLE
        };
        var result = TypeMapping.ResolveColumnTypes(new[] { "BIGINT" }, columns);
        Assert.Equal(new[] { DuckyColumnType.BigInt, DuckyColumnType.Double }, result);
    }

    // ---------- DuckyColumnType parser ----------

    [Theory]
    [InlineData("BOOLEAN",   DuckyColumnType.Boolean)]
    [InlineData("bool",      DuckyColumnType.Boolean)]
    [InlineData("INT",       DuckyColumnType.Integer)]
    [InlineData("integer",   DuckyColumnType.Integer)]
    [InlineData("TINYINT",   DuckyColumnType.Integer)]
    [InlineData("SMALLINT",  DuckyColumnType.Integer)]
    [InlineData("BIGINT",    DuckyColumnType.BigInt)]
    [InlineData("HUGEINT",   DuckyColumnType.BigInt)]
    [InlineData("DOUBLE",    DuckyColumnType.Double)]
    [InlineData("FLOAT",     DuckyColumnType.Double)]
    [InlineData("real",      DuckyColumnType.Double)]
    [InlineData("DECIMAL",   DuckyColumnType.Double)]
    [InlineData("VARCHAR",   DuckyColumnType.Varchar)]
    [InlineData("text",      DuckyColumnType.Varchar)]
    [InlineData("string",    DuckyColumnType.Varchar)]
    [InlineData("TIMESTAMP", DuckyColumnType.Timestamp)]
    [InlineData("datetime",  DuckyColumnType.Timestamp)]
    public void TryParse_KnownNames(string text, DuckyColumnType expected)
    {
        Assert.True(DuckyColumnTypeExtensions.TryParse(text, out var t));
        Assert.Equal(expected, t);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("BLOB")]
    [InlineData("ENUM")]
    public void TryParse_UnknownReturnsFalseAndVarchar(string text)
    {
        Assert.False(DuckyColumnTypeExtensions.TryParse(text, out var t));
        Assert.Equal(DuckyColumnType.Varchar, t);
    }

    [Theory]
    [InlineData(DuckyColumnType.Boolean,   "BOOLEAN")]
    [InlineData(DuckyColumnType.Integer,   "INTEGER")]
    [InlineData(DuckyColumnType.BigInt,    "BIGINT")]
    [InlineData(DuckyColumnType.Double,    "DOUBLE")]
    [InlineData(DuckyColumnType.Timestamp, "TIMESTAMP")]
    [InlineData(DuckyColumnType.Varchar,   "VARCHAR")]
    public void ToSqlText_ReturnsDuckDbKeyword(DuckyColumnType type, string expected)
    {
        Assert.Equal(expected, type.ToSqlText());
    }

    // ---------- helpers ----------

    private static object GetSample(Type t)
    {
        if (t == typeof(bool))    return true;
        if (t == typeof(int))     return 1;
        if (t == typeof(long))    return 1L;
        if (t == typeof(double))  return 1.0;
        if (t == typeof(string))  return "x";
        throw new InvalidOperationException($"No sample for {t}");
    }
}
