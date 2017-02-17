using Iag.Unity.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UnitTests
{
    public class TestLoadClass
    {
        public string StringValue { get; set; }
        public int IntValue { get; set; }
        public double DoubleValue { get; set; }
        public DateTime DateTimeValue { get; set; }
        public DateTimeOffset DateTimeOffsetValue { get; set; }
        public bool BoolValue { get; set; }
    }

    public class TestLoadClassNullable
    {
        public string StringValue { get; set; }
        public int? IntValue { get; set; }
        public double? DoubleValue { get; set; }
        public DateTime? DateTimeValue { get; set; }
        public DateTimeOffset? DateTimeOffsetValue { get; set; }
        public bool? BoolValue { get; set; }
    }

    public class TestLoadClassMapped
    {
        [FieldToProperty("STR")]
        public string StringValue { get; set; }
        [FieldToProperty("INT")]
        public int? IntValue { get; set; }
        [FieldToProperty("DBL")]
        public double? DoubleValue { get; set; }
        [FieldToProperty("DT")]
        public DateTime? DateTimeValue { get; set; }
        [FieldToProperty("DTO")]
        public DateTimeOffset? DateTimeOffsetValue { get; set; }
        [FieldToProperty("B")]
        public bool? BoolValue { get; set; }
    }
}
