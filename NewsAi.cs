using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class NewsAi
{
    public static async Task<string> SummarizeNewsAsync(string userProfile, List<NewsItem> articles)
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY not set");

        var client = new OpenAI.OpenAIClient(apiKey);
        var model = "gpt-5.1"; // Latest model with adaptive reasoning, better for tech summaries

        // Build article context
        var articleContext = new StringBuilder();
        articleContext.AppendLine("ARTICLES TO ANALYZE:");
        articleContext.AppendLine();

        int idx = 1;
        foreach (var article in articles.Take(15))
        {
            articleContext.AppendLine($"[{idx}] **{article.Title}**");
            if (!string.IsNullOrWhiteSpace(article.Summary))
                articleContext.AppendLine($"    Summary: {article.Summary}");
            articleContext.AppendLine($"    Source: {article.Source}");
            articleContext.AppendLine($"    Published: {article.Published:MMM d, yyyy}");
            articleContext.AppendLine();
            idx++;
        }

        var systemPrompt =
            @"You are a technical news summarizer. Follow the output format EXACTLY.

STRICT FORMAT RULES - DO NOT DEVIATE:
1. Output EXACTLY 6-8 bullet points
2. Each bullet MUST follow this exact structure:
   - **[Product/Tech Name vX.X]**: [2-3 sentence summary with specifics]. [Why it matters in 1 sentence]. [Article #]
3. Each bullet must be 2-4 sentences total
4. First sentence: What was released/announced with version number
5. Second sentence: Key technical details (performance, features, breaking changes)
6. Third sentence: Why developers should care
7. Fourth element: Citation [#]
8. Use ONLY markdown bullets (-)
9. NO headers, NO intro, NO outro, NO sections

CONTENT REQUIREMENTS:
- Include exact version numbers (v2.1.3, not ""new version"")
- Include metrics when available (2x faster, 50% smaller)
- Name specific technologies (TypeScript, not ""programming language"")
- Focus on developer-actionable information
- Prioritize: releases > updates > announcements > analysis

EXAMPLE FORMAT:
- **Next.js 15.0**: Released with React Server Components as default, new App Router caching strategy, and 40% faster builds via Turbopack. Breaking change: Pages Router now requires manual opt-in. Critical for teams migrating from Pages to App Router. [3]
- **Claude 3.7 Sonnet**: Anthropic's latest model with 200K context window (2x previous), 88% on SWE-bench coding benchmark (up from 79%), and $3/1M tokens pricing. Available via API today - ideal for code generation and analysis tasks. [7]";

        var userPrompt =
            $@"User interests: {userProfile}

{articleContext}

OUTPUT 6-8 bullets in the EXACT format specified. Each bullet must include:
- **[Product vX.X]**: [What] [Technical details] [Why it matters] [#]

Start output with first bullet. No headers or intro.";

        var chatClient = client.GetChatClient(model);

        List<ChatMessage> messages = new()
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(userPrompt)
        };

        var response = await chatClient.CompleteChatAsync(messages);

        return response.Value.Content[0].Text;
    }
}
