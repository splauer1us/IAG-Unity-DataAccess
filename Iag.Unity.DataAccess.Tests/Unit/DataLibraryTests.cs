using System;
using System.Data;
using Iag.Unity.Core.Enumerations;
using Shouldly;
using Xunit;

namespace Iag.Unity.DataAccess.Tests.Unit
{
    public class DataLibraryTests
    {
        [Theory]
        [InlineData(SqlDbType.BigInt, SimpleDataType.Numeric)]
        [InlineData(SqlDbType.Int, SimpleDataType.Numeric)]
        [InlineData(SqlDbType.Decimal, SimpleDataType.Numeric)]
        [InlineData(SqlDbType.Money, SimpleDataType.Numeric)]
        [InlineData(SqlDbType.Bit, SimpleDataType.Boolean)]
        [InlineData(SqlDbType.UniqueIdentifier, SimpleDataType.Guid)]
        [InlineData(SqlDbType.DateTime, SimpleDataType.DateTime)]
        [InlineData(SqlDbType.DateTime2, SimpleDataType.DateTime)]
        [InlineData(SqlDbType.DateTimeOffset, SimpleDataType.DateTime)]
        [InlineData(SqlDbType.NVarChar, SimpleDataType.String)]
        [InlineData(SqlDbType.VarChar, SimpleDataType.String)]
        public void GetSimpleDataType_FromSqlDbType_MapsCorrectly(SqlDbType input, SimpleDataType expected)
        {
            DataLibrary.GetSimpleDataType(input).ShouldBe(expected);
        }

        [Theory]
        [InlineData(typeof(int), SimpleDataType.Numeric)]
        [InlineData(typeof(int?), SimpleDataType.Numeric)]
        [InlineData(typeof(long), SimpleDataType.Numeric)]
        [InlineData(typeof(decimal?), SimpleDataType.Numeric)]
        [InlineData(typeof(bool), SimpleDataType.Boolean)]
        [InlineData(typeof(bool?), SimpleDataType.Boolean)]
        [InlineData(typeof(Guid), SimpleDataType.Guid)]
        [InlineData(typeof(DateTime), SimpleDataType.DateTime)]
        [InlineData(typeof(DateTimeOffset?), SimpleDataType.DateTime)]
        [InlineData(typeof(string), SimpleDataType.String)]
        public void GetSimpleDataType_FromClrType_MapsCorrectly(Type input, SimpleDataType expected)
        {
            DataLibrary.GetSimpleDataType(input).ShouldBe(expected);
        }

        [Theory]
        [InlineData(SqlDbType.Int, typeof(int))]
        [InlineData(SqlDbType.BigInt, typeof(long))]
        [InlineData(SqlDbType.SmallInt, typeof(short))]
        [InlineData(SqlDbType.TinyInt, typeof(byte))]
        [InlineData(SqlDbType.Decimal, typeof(decimal))]
        [InlineData(SqlDbType.Money, typeof(decimal))]
        [InlineData(SqlDbType.Float, typeof(double))]
        [InlineData(SqlDbType.Bit, typeof(bool))]
        [InlineData(SqlDbType.UniqueIdentifier, typeof(Guid))]
        [InlineData(SqlDbType.DateTime, typeof(DateTime))]
        [InlineData(SqlDbType.DateTimeOffset, typeof(DateTimeOffset))]
        [InlineData(SqlDbType.NVarChar, typeof(string))]
        public void GetDotNetType_MapsSqlTypeToClrType(SqlDbType input, Type expected)
        {
            DataLibrary.GetDotNetType(input).ShouldBe(expected);
        }
    }
}
