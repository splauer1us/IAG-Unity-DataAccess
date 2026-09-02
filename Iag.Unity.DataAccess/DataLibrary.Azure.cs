using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using System.Threading;

namespace Iag.Unity.DataAccess
{
    /// <summary>
    /// Microsoft Entra ID (Azure AD) authentication for the default connection.
    /// </summary>
    /// <remarks>
    /// This file is excluded from the net462 build, where the netstandard2.0 asset of
    /// Azure.Identity would require extensive binding redirects. .NET Framework consumers on
    /// 4.7.2 or later resolve the net472 asset and have these APIs; 4.6.2 - 4.7.1 consumers do not.
    /// </remarks>
    public static partial class DataLibrary
    {
        /// <summary>
        /// Token scope for Azure SQL Database and SQL Managed Instance in the public cloud.
        /// Sovereign clouds use a different audience - set <see cref="AzureSqlScope"/> for those.
        /// </summary>
        public const string DefaultAzureSqlScope = "https://database.windows.net/.default";

        /// <summary>
        /// The credential supplied to <c>InitializeWithAzureCredentials</c>, or null when the
        /// library was initialized with an ordinary connection string.
        /// </summary>
        public static TokenCredential AzureCredential { get; private set; }

        /// <summary>
        /// Scope requested when acquiring an access token. Defaults to
        /// <see cref="DefaultAzureSqlScope"/>; change it before initializing for a sovereign cloud
        /// (for example <c>https://database.usgovcloudapi.net/.default</c>).
        /// </summary>
        public static string AzureSqlScope { get; set; } = DefaultAzureSqlScope;

        /// <summary>
        /// Initialize the default connection using <see cref="DefaultAzureCredential"/>.
        /// </summary>
        /// <remarks>
        /// <see cref="DefaultAzureCredential"/> probes a chain of sources (environment, workload
        /// identity, managed identity, Visual Studio, Azure CLI, Azure PowerShell) and the first
        /// acquisition can take seconds. In production prefer passing a specific credential -
        /// <c>ManagedIdentityCredential</c>, or a <see cref="DefaultAzureCredential"/> built with
        /// <see cref="DefaultAzureCredentialOptions"/> that excludes the sources you don't use.
        /// </remarks>
        public static void InitializeWithAzureCredentials(string connectionString, Action<Exception> loggingCallback = null)
        {
            InitializeWithAzureCredentials(new DefaultAzureCredential(), connectionString, loggingCallback);
        }

        /// <summary>
        /// Initialize the default connection with an Entra ID credential. The credential - not a
        /// token - is stored, and a fresh access token is acquired for each connection as it opens,
        /// so the library keeps working past the ~1 hour lifetime of any single token.
        /// </summary>
        /// <param name="tokenCredential">
        /// Any <see cref="TokenCredential"/>, for example <see cref="DefaultAzureCredential"/>.
        /// Azure.Identity credentials cache tokens in memory and refresh near expiry, so the
        /// per-connection call is cheap after the first.
        /// </param>
        /// <param name="connectionString">
        /// A connection string with no credentials of its own. <c>Integrated Security</c>,
        /// <c>User ID</c>, <c>Password</c> and <c>Authentication</c> are rejected, because
        /// SqlClient will not accept an access token alongside them.
        /// </param>
        /// <param name="loggingCallback">Optional callback for internal diagnostics.</param>
        public static void InitializeWithAzureCredentials(TokenCredential tokenCredential, string connectionString, Action<Exception> loggingCallback = null)
        {
            string validated = PrepareAzureAuthentication(tokenCredential, connectionString);
            InitializeCore(validated, loggingCallback);
        }

        /// <summary>
        /// Initialize the default connection with an Entra ID credential, building the connection
        /// string from a server and database name.
        /// </summary>
        public static void InitializeWithAzureCredentials(TokenCredential tokenCredential, string serverName, string databaseName, Action<Exception> loggingCallback = null)
        {
            InitializeWithAzureCredentials(tokenCredential, BuildAzureConnectionString(serverName, databaseName), loggingCallback);
        }

        /// <summary>
        /// Asynchronous counterpart to
        /// <see cref="InitializeWithAzureCredentials(TokenCredential, string, Action{Exception})"/>.
        /// Prefer it: the connection test opens with <c>OpenAsync</c>, so the first token
        /// acquisition - which can be slow, and which a synchronous <c>Open()</c> blocks on - does
        /// not tie up the calling thread.
        /// </summary>
        public static async Task InitializeWithAzureCredentialsAsync(TokenCredential tokenCredential, string connectionString, Action<Exception> loggingCallback = null, CancellationToken cancellationToken = default)
        {
            string validated = PrepareAzureAuthentication(tokenCredential, connectionString);

            try
            {
                LoggingCallback = loggingCallback ?? ((ex) => { });
                DataLibrary.ConnectionString = validated;

                //Test the connection.
                using (SqlConnection conn = GetConnection(doNotOpen: true))
                {
                    await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
                    IsInitialized = true;
                }
            }
            catch (Exception)
            {
                IsInitialized = false;
                throw;
            }
        }

        /// <summary>
        /// Asynchronous counterpart to
        /// <see cref="InitializeWithAzureCredentials(string, Action{Exception})"/>.
        /// </summary>
        public static Task InitializeWithAzureCredentialsAsync(string connectionString, Action<Exception> loggingCallback = null, CancellationToken cancellationToken = default)
        {
            return InitializeWithAzureCredentialsAsync(new DefaultAzureCredential(), connectionString, loggingCallback, cancellationToken);
        }

        /// <summary>
        /// Asynchronous counterpart to
        /// <see cref="InitializeWithAzureCredentials(TokenCredential, string, string, Action{Exception})"/>.
        /// </summary>
        public static Task InitializeWithAzureCredentialsAsync(TokenCredential tokenCredential, string serverName, string databaseName, Action<Exception> loggingCallback = null, CancellationToken cancellationToken = default)
        {
            return InitializeWithAzureCredentialsAsync(tokenCredential, BuildAzureConnectionString(serverName, databaseName), loggingCallback, cancellationToken);
        }

        static partial void ClearAzureCredential()
        {
            AzureCredential = null;
        }

        // Validates the connection string and arms the token callback. Shared by the sync and
        // async initializers, which differ only in how they open the test connection.
        private static string PrepareAzureAuthentication(TokenCredential tokenCredential, string connectionString)
        {
            if (tokenCredential == null)
                throw new ArgumentNullException(nameof(tokenCredential));

            string validated = ValidateAzureConnectionString(connectionString);

            AzureCredential = tokenCredential;
            ConnectionConfigurator = AttachAccessTokenCallback;

            return validated;
        }

        private static string BuildAzureConnectionString(string serverName, string databaseName)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = serverName,
                InitialCatalog = databaseName,
                Encrypt = SqlConnectionEncryptOption.Mandatory
            };

            return builder.ConnectionString;
        }

        private static string ValidateAzureConnectionString(string connectionString)
        {
            if (String.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A connection string is required.", nameof(connectionString));

            // Round-tripping through the builder also normalizes the keywords we inspect below,
            // so an alias such as "Trusted_Connection" is caught as well.
            var builder = new SqlConnectionStringBuilder(connectionString);

            // SqlClient throws if an access token is supplied alongside any other credential.
            // Fail here instead, where the message can say what to remove.
            if (builder.IntegratedSecurity)
                throw new ArgumentException("A connection string used with an Azure credential must not set 'Integrated Security'. Remove it and let the credential authenticate.", nameof(connectionString));

            if (!String.IsNullOrEmpty(builder.UserID) || !String.IsNullOrEmpty(builder.Password))
                throw new ArgumentException("A connection string used with an Azure credential must not set 'User ID' or 'Password'. Remove them and let the credential authenticate.", nameof(connectionString));

            if (builder.Authentication != SqlAuthenticationMethod.NotSpecified)
                throw new ArgumentException("A connection string used with an Azure credential must not set 'Authentication'. The supplied TokenCredential acquires the access token instead.", nameof(connectionString));

            if (String.IsNullOrWhiteSpace(builder.DataSource))
                throw new ArgumentException("A connection string used with an Azure credential must specify a server ('Server' or 'Data Source').", nameof(connectionString));

            // Never send a bearer token over an unencrypted connection. Mandatory is already the
            // SqlClient default; this only upgrades a string that explicitly opted out. An explicit
            // Encrypt=Strict is stronger and left alone.
            if (builder.Encrypt == SqlConnectionEncryptOption.Optional)
                builder.Encrypt = SqlConnectionEncryptOption.Mandatory;

            return builder.ConnectionString;
        }

        private static void AttachAccessTokenCallback(SqlConnection connection)
        {
            // Captured per connection so a later re-initialization cannot change the credential
            // or scope used by a connection that is already in flight.
            TokenCredential credential = AzureCredential;
            if (credential == null)
                return;

            string scope = String.IsNullOrWhiteSpace(AzureSqlScope) ? DefaultAzureSqlScope : AzureSqlScope;

            // AccessTokenCallback rather than the AccessToken string: SqlClient invokes it as
            // tokens expire and accounts for the credential in the pool key, so pooled connections
            // stay valid for the life of the process.
            connection.AccessTokenCallback = async (parameters, cancellationToken) =>
            {
                try
                {
                    var token = await credential
                        .GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
                        .ConfigureAwait(false);

                    return new SqlAuthenticationToken(token.Token, token.ExpiresOn);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // The token itself is never logged or attached to the exception.
                    var wrapped = new InvalidOperationException(
                        String.Format(
                            "Failed to acquire an Azure SQL access token from {0} for scope '{1}'.",
                            credential.GetType().Name,
                            scope),
                        ex);

                    LoggingCallback?.Invoke(wrapped);
                    throw wrapped;
                }
            };
        }
    }
}
