using Xunit;

namespace Iag.Unity.DataAccess.Tests.Fixtures
{
    [CollectionDefinition(Name)]
    public class DatabaseCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "Database";
    }
}
