// MiniMaxT2AClient.cs

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

/// <summary>
/// 一个用于调用 MiniMax T2A (Text-to-Audio) V2 API 并播放返回音频的独立客户端。
/// 无需任何第三方 NuGet 包。
/// 注意：音频播放功能使用了 P/Invoke 调用 Windows 'winmm.dll'，因此仅在 Windows 平台上有效。
/// </summary>
public class MiniMaxT2AClient
{
    // 使用 P/Invoke 导入 Windows 多媒体库中的 mciSendString 函数
    // 该函数可以发送指令给多媒体控制接口(MCI)，用于播放 mp3 等文件
    [DllImport("winmm.dll")]
    private static extern long mciSendString(string lpstrCommand, StringBuilder lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

    private readonly HttpClient _httpClient;
    private const string ApiBaseUrl = "https://api.minimaxi.com/v1/";

    /// <summary>
    /// 初始化 MiniMaxT2AClient
    /// </summary>
    /// <param name="apiKey">你的 MiniMax API Key</param>
    public MiniMaxT2AClient(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentNullException(nameof(apiKey), "API key cannot be null or empty.");
        }

        _httpClient = new HttpClient();
        // 设置默认的 Authorization 头，这样每次请求就不需要再设置了
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    /// <summary>
    /// 主入口函数：发送文本到 MiniMax API，生成语音并直接播放。
    /// </summary>
    /// <param name="groupId">你的 Group ID</param>
    /// <param name="textToSpeak">需要转换成语音的文本</param>
    /// <returns>返回 API 的响应信息。成功时通常是一个成功状态描述；失败时则包含错误信息。</returns>
    public async Task<string> GenerateAndPlaySpeechAsync(string groupId, string textToSpeak)
    {
        if (string.IsNullOrWhiteSpace(groupId))
        {
            return "Error: Group ID cannot be null or empty.";
        }
        if (string.IsNullOrWhiteSpace(textToSpeak))
        {
            return "Error: Text to speak cannot be null or empty.";
        }

        string requestUrl = $"{ApiBaseUrl}t2a_v2?GroupId={groupId}";

        // 1. 构建请求体 (JSON)
        // 使用匿名对象快速创建，也可以定义专门的请求模型类
        var requestPayload = new
        {
            model = "speech-2.5-turbo-preview",
            text = textToSpeak,
            stream = false,
            timber_weights = new[]
    {
        new { voice_id = "Chinese (Mandarin)_News_Anchor", weight = 1 }
    },
            voice_setting = new
            {
                voice_id = "",
                speed = 1.0,
                vol = 1.0,
                pitch = 0.0,
                emotion = "" // 根据需要添加或移除情感等参数
            },
            audio_setting = new
            {
                sample_rate = 24000, // 常见的采样率
                bitrate = 128000,
                format = "mp3",
                channel = 1
            }
            // pronunciation_dict 根据需要添加
        };

        // 序列化为 JSON 字符串
        string jsonPayload = JsonSerializer.Serialize(requestPayload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            // 2. 发送 POST 请求
            HttpResponseMessage response = await _httpClient.PostAsync(requestUrl, content);

            // 3. 处理响应
            if (response.IsSuccessStatusCode)
            {
                // API 成功时直接返回音频的二进制流
                string responseBody = await response.Content.ReadAsStringAsync();





                if (response.IsSuccessStatusCode)
                {
                    // 3. 解析 JSON 响应
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var apiResponse = JsonSerializer.Deserialize<MiniMaxResponse>(responseBody, options);

                    // 检查响应是否真的成功
                    if (apiResponse?.BaseResp?.StatusCode == 0 && !string.IsNullOrEmpty(apiResponse.Data?.Audio))
                    {
                        // 4. 从 Base64 字符串解码音频数据
                        byte[] audioData = Convert.FromHexString(apiResponse.Data.Audio);

                        // 5. 播放音频
                        PlayMp3FromBytes(audioData);

                        return $"Success! Audio is playing. Trace ID: {apiResponse.TraceId}";
                    }
                    else
                    {
                        // API 返回了 200 OK，但 JSON 内容表示有错误
                        return $"API Error: {apiResponse?.BaseResp?.StatusMsg ?? "Unknown error in response body."}. Trace ID: {apiResponse?.TraceId}";
                    }
                }
                else
                {
                    // 处理 HTTP 错误 (e.g., 4xx, 5xx)
                    return $"HTTP Error: {response.StatusCode}. Details: {responseBody}";
                }

                // API的响应正文是音频文件，但头信息中可能包含有用的元数据
                string traceId = response.Headers.Contains("X-Request-Id") ? string.Join(",", response.Headers.GetValues("X-Request-Id")) : "N/A";
                return $"Success! Audio is playing. Trace ID: {traceId}";
            }
            else
            {
                // 如果 API 返回错误，响应体通常是包含错误信息的 JSON
                string errorContent = await response.Content.ReadAsStringAsync();
                return $"Error: {response.StatusCode}. Details: {errorContent}";
            }
        }
        catch (Exception ex)
        {
            return $"An unexpected error occurred: {ex.Message}";
        }
    }

    /// <summary>
    /// 辅助函数：将 MP3 字节数组保存到临时文件并播放
    /// </summary>
    /// <param name="mp3Bytes">包含 MP3 数据的字节数组</param>
    private void PlayMp3FromBytes(byte[] mp3Bytes)
    {
        // 创建一个唯一的临时文件名
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");

        try
        {
            // 将字节数组写入临时文件
            File.WriteAllBytes(tempFilePath, mp3Bytes);

            // 使用 MCI 命令播放 MP3 文件
            string alias = "minimax_audio";
            string openCommand = $"open \"{tempFilePath}\" type mpegvideo alias {alias}";
            string playCommand = $"play {alias}";
            string closeCommand = $"close {alias}";

            mciSendString(openCommand, null, 0, IntPtr.Zero);
            mciSendString(playCommand, null, 0, IntPtr.Zero);

            // 等待音频播放完毕
            // 这是一个简单的实现：查询状态直到播放停止
            StringBuilder status = new StringBuilder(128);
            string statusCommand = $"status {alias} mode";
            do
            {
                mciSendString(statusCommand, status, status.Capacity, IntPtr.Zero);
                // 等待一小段时间再检查，避免 CPU 占用过高
                Task.Delay(500).Wait();
            } while (status.ToString() == "playing");

            // 关闭设备
            mciSendString(closeCommand, null, 0, IntPtr.Zero);
        }
        finally
        {
            // 确保无论成功还是失败，都删除临时文件
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
    // --- Helper classes for JSON Deserialization ---
    // 这些内部类用于将 API 返回的 JSON 字符串映射为 C# 对象
    private class MiniMaxResponse
    {
        public AudioData Data { get; set; }
        [JsonPropertyName("extra_info")]
        public object ExtraInfo { get; set; } // 我们不关心具体内容，设为 object
        [JsonPropertyName("trace_id")]
        public string TraceId { get; set; }
        [JsonPropertyName("base_resp")]
        public BaseResponse BaseResp { get; set; }
    }

    private class AudioData
    {
        public string Audio { get; set; } // Base64 encoded audio string
        public int Status { get; set; }
    }

    private class BaseResponse
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }
        [JsonPropertyName("status_msg")]
        public string StatusMsg { get; set; }
    }
}