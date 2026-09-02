using Xunit;

// DataLibrary is static: ConnectionString, LoggingCallback, AzureCredential and the connection
// configurator are process-wide. Running collections in parallel would let the Azure credential
// tests and the database fixture overwrite each other's state, so the assembly runs serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
