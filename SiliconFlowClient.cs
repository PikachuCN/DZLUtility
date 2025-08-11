using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/// <summary>
/// 一个独立的客户端，用于与兼容OpenAI标准的SiliconFlow API进行交互。
/// 该类不依赖任何外部NuGet包，仅使用.NET内置功能。
/// </summary>
public static class SiliconFlowClient
{
    // 使用静态HttpClient实例以获得更好的性能和资源管理。
    private static readonly HttpClient httpClient = new HttpClient();
    private const string ApiBaseUrl = "https://api.siliconflow.cn/v1/chat/completions";

    #region Public Entry Method

    /// <summary>
    /// 调用SiliconFlow大模型并获取返回结果。
    /// </summary>
    /// <param name="apiKey">您的SiliconFlow API密钥。</param>
    /// <param name="prompt">您要发送给模型的用户提示。</param>
    /// <param name="model">要使用的模型名称，例如 "Qwen/Qwen2.5-Coder-32B-Instruct"。</param>
    /// <param name="temperature">控制生成文本的随机性，值越高结果越随机。</param>
    /// <param name="maxTokens">生成的最大令牌数。</param>
    /// <returns>返回模型生成的文本内容。如果发生错误，则返回null或抛出异常。</returns>
    public static async Task<string> GetChatCompletionAsync(
        string apiKey,
        string prompt,
        string model = "Qwen/Qwen2.5-Coder-32B-Instruct",
        double temperature = 0.7,
        int maxTokens = 4096)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty.");
        }

        // 1. 设置请求头，包含API Key
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // 2. 构建请求体 (Payload)
        var requestPayload = new ChatRequest
        {
            Model = model,
            Messages = new List<RequestMessage>
            {
                new RequestMessage { Role = "user", Content = prompt }
            },
            Temperature = temperature,
            MaxTokens = maxTokens
        };

        // 3. 将请求对象序列化为JSON字符串
        string jsonPayload = JsonSerializer.Serialize(requestPayload, JsonOptions.Default);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            // 4. 发送POST请求
            HttpResponseMessage response = await httpClient.PostAsync(ApiBaseUrl, content);

            // 5. 检查响应状态并处理结果
            response.EnsureSuccessStatusCode(); // 如果状态码不是2xx，则抛出异常

            string responseBody = await response.Content.ReadAsStringAsync();

            // 6. 反序列化响应JSON并提取内容
            var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseBody, JsonOptions.Default);

            if (chatResponse?.Choices != null && chatResponse.Choices.Count > 0)
            {
                return chatResponse.Choices[0].Message?.Content;
            }

            return "No content received from the model.";
        }
        catch (HttpRequestException e)
        {
            // 处理网络请求相关的错误
            Console.WriteLine($"Request error: {e.Message}");
            // 在实际应用中，您可能希望记录日志或以更优雅的方式处理错误
            throw; 
        }
        catch (JsonException e)
        {
            // 处理JSON解析相关的错误
            Console.WriteLine($"JSON parsing error: {e.Message}");
            throw;
        }
        catch (Exception e)
        {
            // 处理其他未知错误
            Console.WriteLine($"An unexpected error occurred: {e.Message}");
            throw;
        }
    }

    #endregion

    #region JSON Data Structures

    // 用于序列化和反序列化的辅助类
    // 使用 [JsonPropertyName] 来匹配JSON字段（例如 max_tokens）

    private class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("messages")]
        public List<RequestMessage> Messages { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private class RequestMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    private class ChatResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; }
    }

    private class Choice
    {
        [JsonPropertyName("message")]
        public ResponseMessage Message { get; set; }
    }

    private class ResponseMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    // 配置JsonSerializer以忽略null值，这在API请求中是常见的做法
    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    #endregion

    #region Example Usage
    /*
    // 这是一个如何调用该方法的示例
    public static async Task Main(string[] args)
    {
        string apiKey = "YOUR_KEY"; // 请替换为您的真实API Key
        string userPrompt = "编写Python异步爬虫教程，包含代码示例和注意事项";

        Console.WriteLine("Sending request to SiliconFlow API...");

        string result = await GetChatCompletionAsync(apiKey, userPrompt);

        Console.WriteLine("\n--- Model Response ---");
        Console.WriteLine(result);
        Console.WriteLine("----------------------");
    }
    */
    #endregion
}