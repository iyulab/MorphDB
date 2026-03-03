using MorphDB.Core.Models;
using MorphDB.Npgsql.Infrastructure;

namespace MorphDB.Tests.Unit;

public class TypeMapperTests
{
    [Theory]
    [InlineData(MorphDataType.Integer, MorphDataType.BigInteger, true)]
    [InlineData(MorphDataType.Integer, MorphDataType.Decimal, true)]
    [InlineData(MorphDataType.BigInteger, MorphDataType.Decimal, true)]
    [InlineData(MorphDataType.Date, MorphDataType.DateTime, true)]
    [InlineData(MorphDataType.Integer, MorphDataType.Text, true)]
    [InlineData(MorphDataType.Boolean, MorphDataType.Text, true)]
    [InlineData(MorphDataType.Text, MorphDataType.Text, true)]
    public void IsTypeCastSafe_SafeConversions_ReturnsTrue(
        MorphDataType from, MorphDataType to, bool expected)
    {
        TypeMapper.IsTypeCastSafe(from, to).Should().Be(expected);
    }

    [Theory]
    [InlineData(MorphDataType.Text, MorphDataType.Integer)]
    [InlineData(MorphDataType.BigInteger, MorphDataType.Integer)]
    [InlineData(MorphDataType.DateTime, MorphDataType.Date)]
    [InlineData(MorphDataType.Boolean, MorphDataType.Integer)]
    [InlineData(MorphDataType.Json, MorphDataType.Integer)]
    public void IsTypeCastSafe_UnsafeConversions_ReturnsFalse(
        MorphDataType from, MorphDataType to)
    {
        TypeMapper.IsTypeCastSafe(from, to).Should().BeFalse();
    }

    [Fact]
    public void IsTypeCastSafe_SameType_ReturnsTrue()
    {
        TypeMapper.IsTypeCastSafe(MorphDataType.Integer, MorphDataType.Integer).Should().BeTrue();
    }

    [Fact]
    public void IsTypeCastSafe_SameNativeType_ReturnsTrue()
    {
        // Text and Email both map to "text" native type
        TypeMapper.IsTypeCastSafe(MorphDataType.Text, MorphDataType.Email).Should().BeTrue();
    }
}
