using System.Threading.Channels;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AiInExistingApp;

public static class AiRegistration
{
    // --- guide snippet starts (the provider, in one place) ---
    public static IChatClient CreateProviderClient(Uri endpoint, TokenCredential credential, string deploymentName) =>
        new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(deploymentName)
            .AsIChatClient();
    // --- guide snippet ends ---

    // --- guide snippet starts (dependency injection) ---
    public static IServiceCollection AddAiFeatures(this IServiceCollection services, IChatClient providerClient)
    {
        // One pipeline for the whole application: tool calls, telemetry, then the provider.
        services.AddChatClient(providerClient)
            .UseFunctionInvocation()
            .UseOpenTelemetry(sourceName: AiUsage.SourceName);

        // Use cases are ordinary scoped services: they receive IChatClient like any dependency.
        services.AddScoped<RequestTriageService>();
        services.AddScoped<OrderTools>();
        services.AddScoped<AIAgent>(sp =>
            SupportAgent.Create(sp.GetRequiredService<IChatClient>(), sp.GetRequiredService<OrderTools>()));

        // Background triage: a bounded queue applies back-pressure instead of losing messages.
        services.AddSingleton(Channel.CreateBounded<IncomingRequest>(capacity: 100));
        services.AddSingleton(sp => sp.GetRequiredService<Channel<IncomingRequest>>().Reader);
        services.AddHostedService<TriageWorker>();

        return services;
    }
    // --- guide snippet ends ---
}
