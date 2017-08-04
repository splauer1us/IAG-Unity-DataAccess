using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Iag.Unity.DataAccess;
using System.Collections.Generic;
using System.Linq;
using UnitTests;

namespace Unit_Tests
{
    [TestClass]
    public class UnitTest1
    {



        [TestInitialize]
        public void Initialize()
        {
            DataLibrary.Initialize("Database=GTS_UnitySystem;Server=iagunity-dev.database.windows.net;User Id=unitysystem;Password=In$pired1");
        }

        [TestMethod]
        public void TestTransactionCommit()
        {
            using (UnitySqlCommand cmd1 = new UnitySqlCommand("SELECT 1"))
            using (UnitySqlCommand cmd2 = new UnitySqlCommand("SELECT 2"))
            using (UnitySqlCommand cmd3 = new UnitySqlCommand("SELECT 3"))
            using (UnitySqlCommand cmd4 = new UnitySqlCommand("SELECT 4"))
            {
                var trans = UnitySqlCommand.CreateTransaction(cmd1, cmd2, cmd3, cmd4);
                cmd1.Execute();
                cmd2.Execute();
                cmd3.Execute();
                cmd4.Execute();

                trans.Rollback();
            }
        }

        [TestMethod]
        public void TestVanilla()
        {
            using (UnitySqlCommand sp = new UnitySqlCommand("SELECT CAST('2015-12-12 12:12:12 -08:00' as datetimeoffset) as 'DateTimeOffsetValue', 'string' as StringValue, 123 as IntValue, 12.34 as DoubleValue, '2015-10-12 12:12:44' as DateTimeValue, 1 as BoolValue"))
            {
                List<TestLoadClass> tlc = sp.GetObjects<TestLoadClass>().ToList();
                Assert.AreEqual(tlc.Count, 1);
                var obj = tlc.First();
                Assert.AreEqual(obj.StringValue, "string");
                Assert.AreEqual(obj.IntValue, 123);
                Assert.AreEqual(obj.DoubleValue, 12.34);
                Assert.AreEqual(obj.BoolValue, true);
                Assert.AreEqual(obj.DateTimeValue, new DateTime(2015, 10, 12, 12, 12, 44));
                Assert.AreEqual(obj.DateTimeOffsetValue, new DateTimeOffset(new DateTime(2015, 12, 12, 12, 12, 12), TimeSpan.FromHours(-8)));
            }
        }

        [TestMethod]
        public void TestNullable()
        {
            using (UnitySqlCommand sp = new UnitySqlCommand("SELECT NULL as DateTimeOffsetValue, NULL as StringValue, NULL as IntValue, NULL as DoubleValue, NULL as DateTimeValue, NULL as BoolValue"))
            {
                List<TestLoadClassNullable> tlc = sp.GetObjects<TestLoadClassNullable>().ToList();
                Assert.AreEqual(tlc.Count, 1);
                var obj = tlc.First();
                Assert.AreEqual(obj.StringValue, null);
                Assert.AreEqual(obj.IntValue, null);
                Assert.AreEqual(obj.DoubleValue, null);
                Assert.AreEqual(obj.BoolValue, null);
                Assert.AreEqual(obj.DateTimeValue, null);
            }
        }

        [TestMethod]
        public void TestMapped()
        {
            using (UnitySqlCommand sp = new UnitySqlCommand("SELECT 'string' as STR, 123 as INT, 12.34 as DBL, CAST('2015-10-12 12:12:44' as DateTime) as DT, CAST('2015-12-12 12:12:12 -08:00' as DateTimeOffset) as DTO, 0 as B"))
            {
                List<TestLoadClassMapped> tlc = sp.GetObjects<TestLoadClassMapped>().ToList();
                Assert.AreEqual(tlc.Count, 1);
                var obj = tlc.First();
                Assert.AreEqual(obj.StringValue, "string");
                Assert.AreEqual(obj.IntValue, 123);
                Assert.AreEqual(obj.DoubleValue, 12.34);
                Assert.AreEqual(obj.BoolValue, false);
                Assert.AreEqual(obj.DateTimeValue, new DateTime(2015, 10, 12, 12, 12, 44));
                Assert.AreEqual(obj.DateTimeOffsetValue, new DateTimeOffset(new DateTime(2015, 12, 12, 12, 12, 12), TimeSpan.FromHours(-8)));
            }
        }

        [TestMethod]
        public void TestMapFunction()
        {
            using (UnitySqlCommand sp = new UnitySqlCommand("SELECT 'string' as STR, 123 as INT, 12.34 as DBL, CAST('2015-10-12 12:12:44' as DateTime) as DT, CAST('2015-12-12 12:12:12 -08:00' as DateTimeOffset) as DTO, 0 as B"))
            {

                Func<string, string> func = (x) =>
                {
                    if (x == "STR") return "StringValue";
                    else if (x == "INT") return "IntValue";
                    return null;
                };

                List<TestLoadClassNullable> tlc = sp.GetObjects<TestLoadClassNullable>(func).ToList();
                Assert.AreEqual(tlc.Count, 1);
                var obj = tlc.First();
                Assert.AreEqual(obj.StringValue, "string");
                Assert.AreEqual(obj.IntValue, 123);
                Assert.IsNull(obj.DoubleValue);
                Assert.IsNull(obj.BoolValue);
                Assert.IsNull(obj.DateTimeValue);
                Assert.IsNull(obj.DateTimeOffsetValue);
            }
        }
    }
}
