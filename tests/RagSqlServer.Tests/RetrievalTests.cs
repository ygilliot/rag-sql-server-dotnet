using RagSqlServer.Security;

namespace RagSqlServer.Tests;

public sealed class RetrievalTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private const string SkipReason = "Set RAG_SQL_CONNECTION_STRING to a SQL Server 2025 instance (see docker-compose.yml).";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_employee_finds_the_hotel_ceiling()
    {
        Assert.SkipUnless(sql.Available, SkipReason);

        var results = await sql.Retriever.SearchAsync("hotel ceiling per night in Paris", new StaticCallerContext("Employee"), top: 3, Ct);

        Assert.NotEmpty(results);
        Assert.Equal("Expense policy > Travel > Hotels", results[0].HeadingPath);
        Assert.Contains("180 euros", results[0].Content);
        Assert.Equal("https://intranet.example/policies/expenses", results[0].SourceUri);
    }

    [Fact]
    public async Task An_employee_never_retrieves_a_human_resources_document()
    {
        Assert.SkipUnless(sql.Available, SkipReason);
        var salaryDocumentId = sql.DocumentIds["salary-review.md"];

        var results = await sql.Retriever.SearchAsync("salary bands salary increase budget", new StaticCallerContext("Employee"), top: 20, Ct);

        Assert.DoesNotContain(results, r => r.DocumentId == salaryDocumentId);
    }

    [Fact]
    public async Task Human_resources_retrieve_the_salary_bands()
    {
        Assert.SkipUnless(sql.Available, SkipReason);

        var results = await sql.Retriever.SearchAsync(
            "salary range per band for a joiner and a workshop lead", new StaticCallerContext("HR"), top: 3, Ct);

        Assert.NotEmpty(results);
        Assert.Equal(sql.DocumentIds["salary-review.md"], results[0].DocumentId);
        Assert.EndsWith("Salary bands", results[0].HeadingPath);
    }

    [Fact]
    public async Task A_caller_with_several_roles_sees_the_union()
    {
        Assert.SkipUnless(sql.Available, SkipReason);

        var results = await sql.Retriever.SearchAsync("payment terms invoice", new StaticCallerContext("Employee", "Purchasing"), top: 20, Ct);

        Assert.Contains(results, r => r.DocumentId == sql.DocumentIds["supplier-contract.md"]);
        Assert.Contains(results, r => r.DocumentId == sql.DocumentIds["expense-policy.md"]);
        Assert.DoesNotContain(results, r => r.DocumentId == sql.DocumentIds["salary-review.md"]);
    }

    [Fact]
    public async Task A_caller_without_roles_gets_nothing()
    {
        Assert.SkipUnless(sql.Available, SkipReason);

        var results = await sql.Retriever.SearchAsync("hotel ceiling", new StaticCallerContext(), top: 5, Ct);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Ingesting_a_document_again_replaces_its_chunks()
    {
        Assert.SkipUnless(sql.Available, SkipReason);
        var documentId = sql.DocumentIds["expense-policy.md"];
        var before = await sql.CountChunksAsync(documentId);

        var markdown = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "data", "expense-policy.md"), Ct);
        var written = await sql.Ingestor.IngestAsync(documentId, "Expense policy", markdown, Ct);

        Assert.Equal(before, written);
        Assert.Equal(before, await sql.CountChunksAsync(documentId));
    }
}
