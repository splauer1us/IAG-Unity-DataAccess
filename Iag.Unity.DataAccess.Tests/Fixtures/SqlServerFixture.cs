using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Testcontainers.MsSql;
using Xunit;

namespace Iag.Unity.DataAccess.Tests.Fixtures
{
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        private readonly MsSqlContainer _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        public string ConnectionString { get; private set; }

        public async Task InitializeAsync()
        {
            await _container.StartAsync();

            var masterConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
            {
                TrustServerCertificate = true
            }.ConnectionString;

            using (var conn = new SqlConnection(masterConnectionString))
            {
                await conn.OpenAsync();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "IF DB_ID('Tests') IS NULL CREATE DATABASE [Tests];";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            ConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
            {
                InitialCatalog = "Tests",
                // Microsoft.Data.SqlClient encrypts by default and the container serves a
                // self-signed certificate, so the connection must accept it explicitly.
                TrustServerCertificate = true
            }.ConnectionString;

            using (var conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();
                await RunAsync(conn, @"
                    CREATE TABLE Widgets (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Name NVARCHAR(100) NOT NULL,
                        CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                    );");

                await RunAsync(conn, @"
                    CREATE PROCEDURE GetWidgetById @Id INT AS
                    BEGIN
                        SET NOCOUNT ON;
                        SELECT Id, Name, CreatedAt FROM Widgets WHERE Id = @Id;
                    END");

                await RunAsync(conn, @"
                    CREATE PROCEDURE CreateWidget @Name NVARCHAR(100), @NewId INT OUTPUT AS
                    BEGIN
                        SET NOCOUNT ON;
                        INSERT INTO Widgets (Name) VALUES (@Name);
                        SET @NewId = CAST(SCOPE_IDENTITY() AS INT);
                    END");

                await RunAsync(conn, @"
                    CREATE PROCEDURE CountWidgets AS
                    BEGIN
                        SET NOCOUNT ON;
                        SELECT COUNT(*) FROM Widgets;
                    END");

                await RunAsync(conn, @"
                    CREATE PROCEDURE ListWidgets AS
                    BEGIN
                        SET NOCOUNT ON;
                        SELECT Id, Name, CreatedAt FROM Widgets ORDER BY Id;
                    END");
            }

            DataLibrary.Initialize(ConnectionString);
        }

        public async Task ResetAsync()
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();
                await RunAsync(conn, "TRUNCATE TABLE Widgets;");
            }
        }

        public Task DisposeAsync() => _container.DisposeAsync().AsTask();

        private static async Task RunAsync(SqlConnection conn, string sql)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}
