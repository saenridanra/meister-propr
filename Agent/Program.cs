using System.ClientModel;
using Azure.AI.OpenAI;
using DotNetEnv;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Responses;
using System;
using System.Collections.Generic;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using System.Linq.Expressions;


Env.Load();

var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_ENDPOINT");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_API_KEY");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("Set AZURE_OPENAI_DEPLOYMENT_NAME");

var instructions = File.ReadAllText("Instructions.md");

var persistentAgentsClient = new PersistentAgentsClient(endpoint, new DefaultAzureCredential());

var agentMetadata = await persistentAgentsClient.Administration.CreateAgentAsync(
    model: deploymentName,
    name: "SyntaxCheckerAgent",
    instructions: instructions,
    tools: [new CodeInterpreterToolDefinition()]);
AIAgent syntaxCheckerAgent = await persistentAgentsClient.GetAIAgentAsync(agentMetadata.Value.Id);

await foreach (var update in syntaxCheckerAgent.RunStreamingAsync("Tell me a one-sentence fun fact."))
{
    Console.Write(update);
}
