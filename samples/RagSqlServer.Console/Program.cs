// RAG on SQL Server 2025 with Microsoft Agent Framework: console sample.
//
// Offline mode (no API key): deterministic hashing embeddings, prints the chunks each role
// may retrieve. With OPENAI_API_KEY set: real embeddings and an Agent Framework agent.
//
//   dotnet run --project samples/RagSqlServer.Console -- "What is the hotel ceiling in Paris?" --role Employee
//
// Environment variables:
//   RAG_SQL_CONNECTION_STRING  defaults to the docker-compose.yml instance
//   OPENAI_API_KEY             switches to real models
//   OPENAI_BASE_URL            optional, e.g. an Azure OpenAI v1 endpoint https://<resource>.openai.azure.com/openai/v1/
//   CHAT_MODEL                 default gpt-5-mini (a deployment name on Azure OpenAI)
//   EMBEDDING_MODEL            default text-embedding-3-small (1536 dimensions, matches the schema)

using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using RagSqlServer.Agents;
using RagSqlServer.Chunking;
using RagSqlServer.Database;
using RagSqlServer.Embeddings;
using RagSqlServer.Ingestion;
using RagSqlServer.Retrieval;
using RagSqlServer.Samples;
using RagSqlServer.Security;

var connectionString = Environment.GetEnvironmentVariable("RAG_SQL_CONNECTION_STRING")
    ?? "Server=localhost,1433;Database=RagSample;User Id=sa;Password=Rag_Sample_2025!;TrustServerCertificate=True";

var question = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
    ?? "What is the hotel ceiling in Paris?";
var roleIndex = Array.IndexOf(args, "--role");
var role = roleIndex >= 0 && roleIndex + 1 < args.Length ? args[roleIndex + 1] : "Employee";

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
OpenAIClient? openAI = null;
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator;
string embeddingModel;

if (string.IsNullOrEmpty(apiKey))
{
    embeddingGenerator = new HashingEmbeddingGenerator();
    embeddingModel = HashingEmbeddingGenerator.ModelId;
    Console.WriteLine("Offline mode: hashing embeddings, no model call. Set OPENAI_API_KEY for the agent.\n");
}
else
{
    var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
    openAI = new OpenAIClient(
        new ApiKeyCredential(apiKey),
        string.IsNullOrEmpty(baseUrl) ? new OpenAIClientOptions() : new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
    embeddingModel = Environment.GetEnvironmentVariable("EMBEDDING_MODEL") ?? "text-embedding-3-small";
    embeddingGenerator = openAI.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator();
}

await SchemaInstaller.EnsureDatabaseAsync(connectionString);
await SchemaInstaller.EnsureCreatedAsync(connectionString);

var ingestor = new DocumentIngestor(connectionString, embeddingGenerator, embeddingModel, new StructuredChunker());
await SampleSeeder.SeedAsync(connectionString, ingestor, Path.Combine(AppContext.BaseDirectory, "data"));

var retriever = new ChunkRetriever(connectionString, embeddingGenerator, embeddingModel);
var caller = new StaticCallerContext(role);

Console.WriteLine($"Question: {question}");
Console.WriteLine($"Caller role: {role}\n");

if (openAI is null)
{
    foreach (var chunk in await retriever.SearchAsync(question, caller, top: 3))
    {
        Console.WriteLine($"[{chunk.Distance:F3}] {chunk.HeadingPath}");
        var preview = chunk.Content.ReplaceLineEndings(" ");
        Console.WriteLine($"        {preview[..Math.Min(120, preview.Length)]}");
    }

    return;
}

var chatModel = Environment.GetEnvironmentVariable("CHAT_MODEL") ?? "gpt-5-mini";
IChatClient chatClient = openAI.GetChatClient(chatModel).AsIChatClient();

AIAgent agent = DocumentAgent.Create(chatClient, retriever, caller);
AgentSession session = await agent.CreateSessionAsync();
AgentResponse response = await agent.RunAsync(question, session);
Console.WriteLine(response.Text);
