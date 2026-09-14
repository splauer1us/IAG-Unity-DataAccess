using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Shouldly;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Iag.Unity.DataAccess.Tests.Unit
{
    /// <summary>
    /// Covers <c>InitializeWithAzureCredentials</c> without an Azure SQL server: the connection
    /// test is expected to fail against an unroutable address, and what is asserted afterwards is
    /// the state it left behind — the validated connection string and the access-token callback
    /// attached to new connections. Invoking that callback with a fake <see cref="TokenCredential"/>
    /// exercises the whole token path.
    /// </summary>
    [Trait("Category", "Unit")]
    public class AzureCredentialTests : IDisposable
    {
        // Port 1 on the loopback address: refuses immediately, so no test waits on a timeout.
        private const string UnreachableServer =
            "Server=tcp:127.0.0.1,1;Database=Db;Connect Timeout=1;TrustServerCertificate=true;";

        private readonly FakeCredential _credential = new FakeCredential();

        public void Dispose()
        {
            // DataLibrary is static; leave it disarmed for whatever runs next.
            DataLibrary.AzureSqlScope = DataLibrary.DefaultAzureSqlScope;
            TryInitialize(() => DataLibrary.Initialize("Server=tcp:127.0.0.1,1;Database=Db;Connect Timeout=1;Integrated Security=true;"));
        }

        // The connection test is expected to throw; the state it leaves behind is what matters.
        private static void TryInitialize(Action initialize)
        {
            try { initialize(); } catch (SqlException) { } catch (InvalidOperationException) { }
        }

        private void ArmWithFakeCredential(string connectionString = UnreachableServer)
        {
            TryInitialize(() => DataLibrary.InitializeWithAzureCredentials(_credential, connectionString));
        }

        private static async Task<SqlAuthenticationToken> InvokeCallbackAsync(SqlConnection connection)
        {
            connection.AccessTokenCallback.ShouldNotBeNull();
            return await connection.AccessTokenCallback(AuthenticationParameters(), CancellationToken.None);
        }

        private static SqlAuthenticationParameters AuthenticationParameters()
        {
            return new SqlAuthenticationParameters(
                SqlAuthenticationMethod.NotSpecified,
                "s.database.windows.net",
                "Db",
                "https://database.windows.net/",
                "https://login.microsoftonline.com/",
                null,
                null,
                Guid.NewGuid(),
                30);
        }

        // ==============================
        // Connection-string validation
        // ==============================

        [Theory]
        [InlineData("Server=tcp:s.database.windows.net;Database=Db;Integrated Security=true;", "Integrated Security")]
        [InlineData("Server=tcp:s.database.windows.net;Database=Db;Trusted_Connection=yes;", "Integrated Security")]
        [InlineData("Server=tcp:s.database.windows.net;Database=Db;User ID=app;Password=p;", "User ID")]
        [InlineData("Server=tcp:s.database.windows.net;Database=Db;Authentication=Active Directory Default;", "Authentication")]
        [InlineData("Database=Db;", "must specify a server")]
        [InlineData("   ", "connection string is required")]
        public void InitializeWithAzureCredentials_WithCredentialsInConnectionString_Throws(string connectionString, string expectedInMessage)
        {
            var ex = Should.Throw<ArgumentException>(
                () => DataLibrary.InitializeWithAzureCredentials(_credential, connectionString));

            ex.Message.ShouldContain(expectedInMessage);
        }

        [Fact]
        public void InitializeWithAzureCredentials_WithNullCredential_Throws()
        {
            Should.Throw<ArgumentNullException>(
                () => DataLibrary.InitializeWithAzureCredentials(null, UnreachableServer));
        }

        [Fact]
        public void InitializeWithAzureCredentials_WithEncryptFalse_UpgradesToMandatory()
        {
            ArmWithFakeCredential(UnreachableServer + "Encrypt=false;");

            new SqlConnectionStringBuilder(DataLibrary.ConnectionString)
                .Encrypt.ShouldBe(SqlConnectionEncryptOption.Mandatory);
        }

        [Fact]
        public void InitializeWithAzureCredentials_WithEncryptStrict_LeavesItAlone()
        {
            ArmWithFakeCredential(UnreachableServer + "Encrypt=Strict;");

            new SqlConnectionStringBuilder(DataLibrary.ConnectionString)
                .Encrypt.ShouldBe(SqlConnectionEncryptOption.Strict);
        }

        [Fact]
        public void InitializeWithAzureCredentials_WithServerAndDatabase_BuildsTheConnectionString()
        {
            TryInitialize(() => DataLibrary.InitializeWithAzureCredentials(_credential, "tcp:127.0.0.1,1", "MyDb"));

            var builder = new SqlConnectionStringBuilder(DataLibrary.ConnectionString);
            builder.DataSource.ShouldBe("tcp:127.0.0.1,1");
            builder.InitialCatalog.ShouldBe("MyDb");
            builder.Encrypt.ShouldBe(SqlConnectionEncryptOption.Mandatory);
        }

        // ==============================
        // State after initialization
        // ==============================

        [Fact]
        public void InitializeWithAzureCredentials_WhenConnectionTestFails_LeavesIsInitializedFalse()
        {
            ArmWithFakeCredential();

            DataLibrary.IsInitialized.ShouldBeFalse();
            DataLibrary.AzureCredential.ShouldBeSameAs(_credential);
        }

        [Fact]
        public void GetConnection_AfterAzureInitialize_AttachesTheAccessTokenCallback()
        {
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
                conn.AccessTokenCallback.ShouldNotBeNull();
        }

        [Fact]
        public void CommandCreatedConnection_AfterAzureInitialize_CarriesTheAccessTokenCallback()
        {
            ArmWithFakeCredential();

            // BaseCommand creates its connection through GetConnection(doNotOpen: true) and opens
            // it on first use, so the callback has to be attached at construction time.
            using (var cmd = new UnitySqlCommand("SELECT 1"))
                cmd.Connection.AccessTokenCallback.ShouldNotBeNull();
        }

        [Fact]
        public void Initialize_AfterAzureInitialize_DisarmsEntraAuthentication()
        {
            ArmWithFakeCredential();

            TryInitialize(() => DataLibrary.Initialize(UnreachableServer + "Integrated Security=true;"));

            DataLibrary.AzureCredential.ShouldBeNull();
            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
                conn.AccessTokenCallback.ShouldBeNull();
        }

        // ==============================
        // The token callback itself
        // ==============================

        [Fact]
        public async Task AccessTokenCallback_ReturnsTheCredentialsToken()
        {
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
            {
                var token = await InvokeCallbackAsync(conn);

                token.AccessToken.ShouldBe(FakeCredential.TokenValue);
                token.ExpiresOn.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(20));
            }
        }

        [Fact]
        public async Task AccessTokenCallback_RequestsTheAzureSqlScope()
        {
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
                await InvokeCallbackAsync(conn);

            _credential.LastScopes.ShouldBe(new[] { DataLibrary.DefaultAzureSqlScope });
        }

        [Fact]
        public async Task AccessTokenCallback_RequestsACustomScopeWhenSet()
        {
            const string sovereignScope = "https://database.usgovcloudapi.net/.default";
            DataLibrary.AzureSqlScope = sovereignScope;
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
                await InvokeCallbackAsync(conn);

            _credential.LastScopes.ShouldBe(new[] { sovereignScope });
        }

        [Fact]
        public async Task AccessTokenCallback_UsesTheCredentialCapturedAtInitialization()
        {
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
            {
                // Re-initializing must not change the credential a connection already holds.
                var replacement = new FakeCredential();
                TryInitialize(() => DataLibrary.InitializeWithAzureCredentials(replacement, UnreachableServer));

                await InvokeCallbackAsync(conn);

                _credential.CallCount.ShouldBe(1);
                replacement.CallCount.ShouldBe(0);
            }
        }

        [Fact]
        public async Task AccessTokenCallback_WhenAcquisitionFails_WrapsWithContextAndLogs()
        {
            Exception logged = null;
            var failing = new ThrowingCredential();
            try
            {
                DataLibrary.InitializeWithAzureCredentials(failing, UnreachableServer, ex => logged = ex);
            }
            catch (SqlException) { }
            catch (InvalidOperationException) { }

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
            {
                var ex = await Should.ThrowAsync<InvalidOperationException>(() => InvokeCallbackAsync(conn));

                ex.Message.ShouldContain(nameof(ThrowingCredential));
                ex.Message.ShouldContain(DataLibrary.DefaultAzureSqlScope);
                ex.InnerException.ShouldBeOfType<AuthenticationFailedException>();

                logged.ShouldNotBeNull();
                logged.ShouldBeOfType<InvalidOperationException>();
            }
        }

        [Fact]
        public async Task AccessTokenCallback_PropagatesCancellation()
        {
            ArmWithFakeCredential();

            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Should.ThrowAsync<OperationCanceledException>(
                    () => conn.AccessTokenCallback(AuthenticationParameters(), cts.Token));
            }
        }

        // ==============================
        // Async initializers
        // ==============================

        [Fact]
        public async Task InitializeWithAzureCredentialsAsync_ThrowsWhenTheServerIsUnreachable()
        {
            await Should.ThrowAsync<SqlException>(
                () => DataLibrary.InitializeWithAzureCredentialsAsync(_credential, UnreachableServer));

            DataLibrary.IsInitialized.ShouldBeFalse();
            using (var conn = DataLibrary.GetConnection(doNotOpen: true))
                conn.AccessTokenCallback.ShouldNotBeNull();
        }

        [Fact]
        public async Task InitializeWithAzureCredentialsAsync_ValidatesTheConnectionStringBeforeConnecting()
        {
            await Should.ThrowAsync<ArgumentException>(
                () => DataLibrary.InitializeWithAzureCredentialsAsync(
                    _credential, "Server=tcp:s.database.windows.net;Database=Db;User ID=app;Password=p;"));
        }

        // ==============================
        // Test doubles
        // ==============================

        private sealed class FakeCredential : TokenCredential
        {
            public const string TokenValue = "fake-token";

            public string[] LastScopes { get; private set; }
            public int CallCount { get; private set; }

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LastScopes = requestContext.Scopes;
                CallCount++;
                return new AccessToken(TokenValue, DateTimeOffset.UtcNow.AddMinutes(45));
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
            }
        }

        private sealed class ThrowingCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new AuthenticationFailedException("no managed identity endpoint found");
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
            }
        }
    }
}
