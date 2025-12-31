using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class ChallengeGenerator
{
    public static async Task<string> GenerateDailyChallengeAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            return string.Empty;

        try
        {
            var client = new OpenAI.OpenAIClient(apiKey);
            var model = "gpt-5.1";

            // Use date as seed for deterministic challenges (same challenge all day)
            var today = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

            var systemPrompt = @"You are a technical challenge generator for full-stack engineers.

Generate ONE coding challenge that helps keep programming skills sharp.

CHALLENGE TYPES (rotate between these):
1. Algorithm problems (sorting, searching, graphs, trees, dynamic programming)
2. Data structure implementations (LRU cache, trie, heap, etc.)
3. System design questions (design a URL shortener, rate limiter, etc.)
4. Code optimization (improve time/space complexity)
5. Real-world scenarios (parse CSV, validate email, implement debounce)
6. Web fundamentals (implement Promise.all, deep clone, event emitter)

OUTPUT FORMAT (strict):
**Challenge of the Day: [Title]**

[2-3 sentence problem description]

**Difficulty:** [Easy/Medium/Hard]

**Skills:** [List 2-3 relevant skills: e.g., Arrays, Hash Tables, Recursion]

**Example:**
```
Input: [example input]
Output: [example output]
```

**Bonus:** [Optional 1-sentence extension to make it harder]

REQUIREMENTS:
- Keep it practical and relevant to real development work
- Be specific with input/output examples
- Don't provide solutions - just the problem
- Difficulty should be doable in 15-30 minutes
- Vary the challenge type based on the date";

            var userPrompt = $@"Generate a unique coding challenge for {today}.

Make it interesting, practical, and appropriate for a full-stack engineer looking to stay sharp.";

            var chatClient = client.GetChatClient(model);

            List<ChatMessage> messages = new()
            {
                ChatMessage.CreateSystemMessage(systemPrompt),
                ChatMessage.CreateUserMessage(userPrompt)
            };

            var response = await chatClient.CompleteChatAsync(messages);
            return response.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Challenge generation failed: {ex.Message}");
            return string.Empty;
        }
    }
}
