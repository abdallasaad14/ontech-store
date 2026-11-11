global using System.IO;
global using System.Net.Http;
global using Xunit;

// The SQL Server trigger test (TR_Audit_FailMovement) breaks any stock-in another class saves at the same moment.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
