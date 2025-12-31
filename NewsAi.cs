using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using OpenAI.Chat;

public static class NewsAi
{
        public static async Task<string> SummarizeWorldNewsAsync(string userProfile)
        {
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY not set");

            var client = new OpenAI.OpenAIClient(apiKey);

            var model = "gpt-4o-mini";

            var systemPrompt =
                @"You generate short, relevant world-news digests.
                - Provide concise, relevant summaries based on user interests.
                - Avoid sensationalism. Prioritize signal over noise.
                - Keep summaries short and sharp without fluff.
                - Provide a 5-bullet digest of what matters IN THE LAST FEW DAYS.";

            var userPrompt =
                $@"Use the model's current global knowledge and general world understanding.
                Generate a customized, personal digest for a user with these interests:
                {userProfile}

                Output in clean Markdown with bullet points.";

            var chatClient = client.GetChatClient(model);

            // Explicitly type the messages collection
            List<ChatMessage> messages = new()
            {
                ChatMessage.CreateSystemMessage(systemPrompt),
                ChatMessage.CreateUserMessage(userPrompt)
            };

            var response = await chatClient.CompleteChatAsync(messages);

            return response.Value.Content[0].Text;
    }
}
