using System;
using System.Linq;
using System.Threading.Tasks;
using Iag.Unity.DataAccess.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Iag.Unity.DataAccess.Tests.Integration
{
    [Collection(DatabaseCollection.Name)]
    [Trait("Category", "Integration")]
    public class StoredProcedureTests : IAsyncLifetime
    {
        private readonly SqlServerFixture _fx;

        public StoredProcedureTests(SqlServerFixture fx) => _fx = fx;

        public Task InitializeAsync() => _fx.ResetAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        // ==============================
        // Tests for asynchronous methods
        // ==============================

        [Fact]
        public async Task ExecuteAsync_WithOutputParameter_PopulatesReturnValue()
        {
            using (var sp = new StoredProcedure("CreateWidget"))
            {
                sp.Prepare();
                sp.Parameters["@Name"] = "test-widget";
                sp.Parameters["@NewId"] = 0;
                await sp.ExecuteAsync();

                sp.Parameters.ShouldContainKey("@NewId");
                sp.Parameters["@NewId"].ShouldBeOfType<int>().ShouldBeGreaterThan(0);
            }
        }

        [Fact]
        public async Task ExecuteScalarAsync_ReturnsTypedValue()
        {
            await InsertWidgetsAsync("a", "b", "c");

            using (var sp = new StoredProcedure("CountWidgets"))
            {
                sp.Prepare();
                var count = await sp.ExecuteScalarAsync<int>(0);
                count.ShouldBe(3);
            }
        }

        [Fact]
        public async Task GetObjectAsync_MapsRowToPocoByPropertyName()
        {
            int id = await InsertWidgetAsync("alpha");

            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = id;
                var widget = await sp.GetObjectAsync<Widget>();

                widget.ShouldNotBeNull();
                widget.Id.ShouldBe(id);
                widget.Name.ShouldBe("alpha");
                widget.CreatedAt.ShouldNotBe(default);
            }
        }

        [Fact]
        public async Task GetObjectsAsync_ReturnsAllRows()
        {
            await InsertWidgetsAsync("one", "two", "three");

            using (var sp = new StoredProcedure("ListWidgets"))
            {
                sp.Prepare();
                var widgets = (await sp.GetObjectsAsync<Widget>()).ToList();

                widgets.Count.ShouldBe(3);
                widgets.Select(w => w.Name).ShouldBe(new[] { "one", "two", "three" });
            }
        }

        [Fact]
        public async Task GetObjectAsync_WithFieldToPropertyAttribute_MapsRenamedColumn()
        {
            int id = await InsertWidgetAsync("renamed");

            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = id;
                var widget = await sp.GetObjectAsync<WidgetWithRenamedProp>();

                widget.ShouldNotBeNull();
                widget.WidgetName.ShouldBe("renamed");
            }
        }

        [Fact]
        public async Task GetObjectAsync_WhenNoMatch_ReturnsNull()
        {
            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = 999999;
                var widget = await sp.GetObjectAsync<Widget>();
                widget.ShouldBeNull();
            }
        }

        // ==============================
        // Tests for synchronous methods
        // ==============================

        [Fact]
        public void Execute_WithOutputParameter_PopulatesReturnValue()
        {
            using (var sp = new StoredProcedure("CreateWidget"))
            {
                sp.Prepare();
                sp.Parameters["@Name"] = "test-widget";
                sp.Parameters["@NewId"] = 0;
                sp.Execute();

                sp.Parameters.ShouldContainKey("@NewId");
                sp.Parameters["@NewId"].ShouldBeOfType<int>().ShouldBeGreaterThan(0);
            }
        }

        [Fact]
        public void ExecuteScalar_ReturnsTypedValue()
        {
            InsertWidgets("a", "b", "c");

            using (var sp = new StoredProcedure("CountWidgets"))
            {
                sp.Prepare();
                var count = sp.ExecuteScalar<int>(0);
                count.ShouldBe(3);
            }
        }

        [Fact]
        public void GetObject_MapsRowToPocoByPropertyName()
        {
            int id = InsertWidget("alpha");

            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = id;
                var widget = sp.GetObject<Widget>();

                widget.ShouldNotBeNull();
                widget.Id.ShouldBe(id);
                widget.Name.ShouldBe("alpha");
                widget.CreatedAt.ShouldNotBe(default);
            }
        }

        [Fact]
        public void GetObjects_ReturnsAllRows()
        {
            InsertWidgets("one", "two", "three");

            using (var sp = new StoredProcedure("ListWidgets"))
            {
                sp.Prepare();
                var widgets = sp.GetObjects<Widget>().ToList();

                widgets.Count.ShouldBe(3);
                widgets.Select(w => w.Name).ShouldBe(new[] { "one", "two", "three" });
            }
        }

        [Fact]
        public void GetObject_WithFieldToPropertyAttribute_MapsRenamedColumn()
        {
            int id = InsertWidget("renamed");

            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = id;
                var widget = sp.GetObject<WidgetWithRenamedProp>();

                widget.ShouldNotBeNull();
                widget.WidgetName.ShouldBe("renamed");
            }
        }

        [Fact]
        public void GetObject_WhenNoMatch_ReturnsNull()
        {
            using (var sp = new StoredProcedure("GetWidgetById"))
            {
                sp.Prepare();
                sp.Parameters["@Id"] = 999999;
                var widget = sp.GetObject<Widget>();
                widget.ShouldBeNull();
            }
        }

        private async Task<int> InsertWidgetAsync(string name)
        {
            using (var sp = new StoredProcedure("CreateWidget"))
            {
                sp.Prepare();
                sp.Parameters["@Name"] = name;
                sp.Parameters["@NewId"] = 0;
                await sp.ExecuteAsync();
                return (int)sp.Parameters["@NewId"];
            }
        }

        private async Task InsertWidgetsAsync(params string[] names)
        {
            foreach (var name in names)
                await InsertWidgetAsync(name);
        }

        private int InsertWidget(string name)
        {
            using (var sp = new StoredProcedure("CreateWidget"))
            {
                sp.Prepare();
                sp.Parameters["@Name"] = name;
                sp.Parameters["@NewId"] = 0;
                sp.Execute();
                return (int)sp.Parameters["@NewId"];
            }
        }

        private void InsertWidgets(params string[] names)
        {
            foreach (var name in names)
                InsertWidget(name);
        }

        public class Widget
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        public class WidgetWithRenamedProp
        {
            public int Id { get; set; }

            [FieldToProperty("Name")]
            public string WidgetName { get; set; }
        }
    }
}
