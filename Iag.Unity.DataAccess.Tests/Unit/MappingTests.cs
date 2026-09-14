using System;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Shouldly;
using Xunit;

namespace Iag.Unity.DataAccess.Tests.Unit
{
    public class MappingTests
    {
        public class FillFromMapping
        {
            [Fact]
            public void SetsValueWhenPropertyNameMatchesColumn()
            {
                var row = BuildRow(
                    ("Id", typeof(int), 42),
                    ("Name", typeof(string), "hello"));

                var obj = Map<SimpleWidget>(row);

                obj.Id.ShouldBe(42);
                obj.Name.ShouldBe("hello");
            }

            [Fact]
            public void UsesRenamedColumnWhenFieldToPropertyAttributePresent()
            {
                var row = BuildRow(("widget_name", typeof(string), "renamed"));

                var obj = Map<WidgetWithRenamedProp>(row);

                obj.WidgetName.ShouldBe("renamed");
            }

            [Fact]
            public void SetsNullWhenDBNullToNullableProperty()
            {
                var row = BuildRow(("Age", typeof(int), DBNull.Value));

                var obj = Map<NullableWidget>(row);

                obj.Age.ShouldBeNull();
            }

            // Behavior change from the version these tests were first written against: mapping a
            // NULL column onto a non-nullable value type used to reach Convert.ChangeType(null, …)
            // and throw InvalidCastException. It now leaves the property at its default, which is
            // the documented contract (docs/index.md, "Types" note under Object mapping).
            [Fact]
            public void LeavesNonNullableValueTypeAtDefaultWhenDBNull()
            {
                var row = BuildRow(("Id", typeof(int), DBNull.Value));

                var obj = Map<SimpleWidget>(row);

                obj.Id.ShouldBe(0);
            }

            [Fact]
            public void SetsNullWhenDBNullToReferenceType()
            {
                var row = BuildRow(("Name", typeof(string), DBNull.Value));

                var obj = Map<SimpleWidget>(row);

                obj.Name.ShouldBeNull();
            }

            [Fact]
            public void IntColumnIsCoercedToLongProperty()
            {
                var row = BuildRow(("Count", typeof(int), 7));

                var obj = Map<CountWidget>(row);

                obj.Count.ShouldBe(7L);
            }

            [Fact]
            public void MatchesCaseInsensitiveWhenNonStrict()
            {
                var row = BuildRow(("NAME", typeof(string), "casey"));

                var obj = Map<SimpleWidget>(row, strict: false);

                obj.Name.ShouldBe("casey");
            }

            [Fact]
            public void DoesNotMatchOnCaseDifferenceWhenStrict()
            {
                var row = BuildRow(("NAME", typeof(string), "casey"));

                var obj = Map<SimpleWidget>(row, strict: true);

                obj.Name.ShouldBeNull();
            }

            [Fact]
            public void LeavesPropertyAtDefaultWhenColumnAbsentAndNoAttribute()
            {
                var row = BuildRow(("Id", typeof(int), 5));

                var obj = Map<SimpleWidget>(row);

                obj.Id.ShouldBe(5);
                obj.Name.ShouldBeNull();
            }

            [Fact]
            public void OverridesValueWhenTranslationHandlerHandled()
            {
                var row = BuildRow(("Name", typeof(string), "raw"));

                var obj = Map<SimpleWidget>(row, translation: th =>
                {
                    if (th.PropertyInfo.Name == nameof(SimpleWidget.Name))
                    {
                        th.TranslatedValue = "translated";
                        th.Handled = true;
                    }
                });

                obj.Name.ShouldBe("translated");
            }

            [Fact]
            public void FallsBackToDefaultConversionWhenTranslationHandlerNotHandled()
            {
                var row = BuildRow(("Name", typeof(string), "raw"));

                var obj = Map<SimpleWidget>(row, translation: _ => { /* observe only, leave Handled=false */ });

                obj.Name.ShouldBe("raw");
            }
        }

        public class FillFromFunction
        {
            [Fact]
            public void MapsViaCallback()
            {
                var row = BuildRow(("col_name", typeof(string), "alpha"));

                var obj = MapWithFunction<SimpleWidget>(row, col => col == "col_name" ? "Name" : null);

                obj.Name.ShouldBe("alpha");
            }

            [Fact]
            public void SkipsColumnWhenNullMapReturn()
            {
                var row = BuildRow(("col_name", typeof(string), "alpha"));

                var obj = MapWithFunction<SimpleWidget>(row, _ => null);

                obj.Name.ShouldBeNull();
            }

            [Fact]
            public void MappingToNonExistentPropertyIsIgnored()
            {
                var row = BuildRow(("col_name", typeof(string), "alpha"));

                var obj = MapWithFunction<SimpleWidget>(row, _ => "PropertyThatDoesNotExist");

                obj.Name.ShouldBeNull();
                obj.Id.ShouldBe(0);
            }
        }

        // The per-row FillFromMapping/FillFromFunction internals these tests were written against
        // were replaced by a single per-table mapping plan (BaseCommand.MapTable), so the helpers
        // now build a one-row DataTable and map that. The assertions are unchanged.
        private static DataTable BuildRow(params (string Column, Type Type, object Value)[] columns)
        {
            var table = new DataTable();
            foreach (var (name, type, _) in columns)
                table.Columns.Add(name, type);

            var row = table.NewRow();
            foreach (var (name, _, value) in columns)
                row[name] = value;
            table.Rows.Add(row);
            return table;
        }

        private static T Map<T>(DataTable table, bool strict = false, Action<TranslationHandler> translation = null) where T : class, new()
        {
            using (var cmd = NewCommand())
                return cmd.MapTable<T>(table, null, translation, strict).Single();
        }

        private static T MapWithFunction<T>(DataTable table, Func<string, string> mapFn, Action<TranslationHandler> translation = null) where T : class, new()
        {
            using (var cmd = NewCommand())
                return cmd.MapTable<T>(table, mapFn, translation, false).Single();
        }

        private static UnitySqlCommand NewCommand() => new UnitySqlCommand(new SqlConnection(), string.Empty);

        public class SimpleWidget
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        public class NullableWidget
        {
            public int? Age { get; set; }
        }

        public class CountWidget
        {
            public long Count { get; set; }
        }

        public class WidgetWithRenamedProp
        {
            [FieldToProperty("widget_name")]
            public string WidgetName { get; set; }
        }
    }
}
