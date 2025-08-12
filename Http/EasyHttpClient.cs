using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;


/// <summary>
/// 一个简单的HTTP客户端封装类，提供GET和POST请求功能
/// 支持Cookie管理、重试机制、自定义请求头等功能
/// </summary>
public class EasyHttpClient
{
    /// <summary>
    /// HTTP请求结果数据封装类
    /// </summary>
    public class HttpResultData
    {
        /// <summary>
        /// 响应的HTML内容或文本数据
        /// </summary>
        public string Html { get; set; } = string.Empty;

        /// <summary>
        /// HTTP响应状态码
        /// </summary>
        public HttpStatusCode StatusCode { get; set; }

        /// <summary>
        /// 原始的HTTP响应对象，包含完整的响应信息（Headers、Cookies等）
        /// </summary>
        public HttpResponseMessage Response { get; set; }

        /// <summary>
        /// 请求过程中的错误信息（如果有）
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 关联的EasyHttpClient实例引用
        /// </summary>
        public EasyHttpClient EasyHttpClient { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="easyHttpClient">关联的EasyHttpClient实例</param>
        public HttpResultData(EasyHttpClient easyHttpClient)
        {
            EasyHttpClient = easyHttpClient ?? throw new ArgumentNullException(nameof(easyHttpClient));
            Response = new HttpResponseMessage();
        }

        /// <summary>
        /// 将Html内容解析为匿名对象
        /// </summary>
        /// <returns>解析后的对象</returns>
        public object? ToJson()
        {
            if (string.IsNullOrWhiteSpace(Html))
            {
                return null;
            }
            try
            {
                return JsonSerializer.Deserialize<object>(Html);
            }
            catch (JsonException)
            {
                // 可以选择记录日志或处理异常
                return null;
            }
        }
    }



    // 私有字段
    private readonly HttpClient _httpClient;
    private readonly CookieContainer _cookieContainer;
    private bool _isTimeoutSet;
    private readonly Dictionary<string, string> _headers;

    /// <summary>
    /// 静态实例，用于便捷调用
    /// </summary>
    public static readonly EasyHttpClient _ = new EasyHttpClient();

    /// <summary>
    /// 获取或设置最大重试次数，默认为1次
    /// </summary>
    public int MaxRetries
    {
        get => _maxRetries;
        set
        {
            if (value < 0)
                throw new ArgumentException("最大重试次数不能小于0", nameof(value));
            _maxRetries = value;
        }
    }
    private int _maxRetries = 1;

    /// <summary>
    /// 获取或设置重试延迟时间，默认为1秒
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 获取或设置Cookie字符串
    /// </summary>
    public string Cookies { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置User-Agent字符串，默认为Chrome浏览器
    /// </summary>
    public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/71.0.3578.98 Safari/537.36";

    /// <summary>
    /// 获取或设置Referer来源地址
    /// </summary>
    public string Referer { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置字符编码，默认为UTF-8
    /// </summary>
    public Encoding Encode { get; set; } = Encoding.UTF8;

    /// <summary>
    /// 获取或设置请求超时时间（毫秒），默认为50秒
    /// 注意：该值只能在第一次请求前设置
    /// </summary>
    public int Timeout
    {
        get => _timeout;
        set
        {
            if (value <= 0)
                throw new ArgumentException("超时时间必须大于0", nameof(value));
            _timeout = value;
        }
    }
    private int _timeout = 50000;

    /// <summary>
    /// 获取或设置HTTP请求方法，默认为GET
    /// </summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>
    /// 获取或设置请求URL
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置Content-Type，默认为text/html
    /// POST请求时默认为application/x-www-form-urlencoded
    /// </summary>
    public string ContentType { get; set; } = "text/html";

    /// <summary>
    /// 获取自定义请求头集合
    /// </summary>
    public Dictionary<string, string> Headers => _headers;

    /// <summary>
    /// 获取或设置POST请求的数据内容
    /// </summary>
    public string PostData { get; set; } = string.Empty;
    /// <summary>
    /// 构造函数，初始化HTTP客户端和Cookie容器
    /// </summary>
    public EasyHttpClient()
    {
        _cookieContainer = new CookieContainer();
        _headers = new Dictionary<string, string>();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        _httpClient = new HttpClient(handler);
        _isTimeoutSet = false;
    }

    /// <summary>
    /// 同步GET请求
    /// </summary>
    /// <param name="url">请求URL</param>
    /// <returns>HTTP响应结果</returns>
    public HttpResultData Get(string url)
    {
        return RunSync(() => GetAsync(url));
    }

    /// <summary>
    /// 同步POST请求
    /// </summary>
    /// <param name="url">请求URL</param>
    /// <param name="data">POST数据</param>
    /// <param name="contenttype">Content-Type，默认为application/x-www-form-urlencoded</param>
    /// <returns>HTTP响应结果</returns>
    public HttpResultData Post(string url, string data, string contenttype = "application/x-www-form-urlencoded")
    {
        return RunSync(() => PostAsync(url, data, contenttype));
    }


    /// <summary>
    /// 异步GET请求
    /// </summary>
    /// <param name="url">请求URL</param>
    /// <returns>HTTP响应结果</returns>
    public async Task<HttpResultData> GetAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("请求URL不能为空", nameof(url));

        Method = HttpMethod.Get;
        Url = url;
        return await Send();
    }

    /// <summary>
    /// 异步POST请求
    /// </summary>
    /// <param name="url">请求URL</param>
    /// <param name="data">POST数据</param>
    /// <param name="contenttype">Content-Type，默认为application/x-www-form-urlencoded</param>
    /// <returns>HTTP响应结果</returns>
    public async Task<HttpResultData> PostAsync(string url, string data, string contenttype = "application/x-www-form-urlencoded")
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("请求URL不能为空", nameof(url));

        Method = HttpMethod.Post;
        PostData = data ?? string.Empty;
        ContentType = contenttype;
        Url = url;
        return await Send();
    }
    /// <summary>
    /// 将异步方法转换为同步执行
    /// 避免死锁问题，使用Task.Run在新线程中执行
    /// </summary>
    /// <typeparam name="TResult">结果类型</typeparam>
    /// <param name="func">异步函数</param>
    /// <returns>执行结果</returns>
    private static TResult RunSync<TResult>(Func<Task<TResult>> func)
    {
        return Task.Run(func).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 发送HTTP请求的核心方法
    /// 支持自动重试、Cookie管理、自定义请求头等
    /// </summary>
    /// <returns>HTTP响应结果</returns>
    public async Task<HttpResultData> Send()
    {
        // 验证URL
        if (string.IsNullOrWhiteSpace(Url))
            throw new InvalidOperationException("URL不能为空");

        var result = new HttpResultData(this);
        int attemptCount = 0;
        Exception lastException = null;

        while (attemptCount <= MaxRetries)
        {
            HttpRequestMessage request = null;
            HttpResponseMessage response = null;

            try
            {
                // 创建请求对象
                request = new HttpRequestMessage(Method, Url);

                // 添加自定义请求头
                foreach (var header in _headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // 添加标准请求头
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                if (!string.IsNullOrWhiteSpace(Referer))
                {
                    request.Headers.TryAddWithoutValidation("Referer", Referer);
                }

                // 设置超时时间（只设置一次）
                if (!_isTimeoutSet)
                {
                    _httpClient.Timeout = TimeSpan.FromMilliseconds(Timeout);
                    _isTimeoutSet = true;
                }

                // 设置Cookies
                if (!string.IsNullOrWhiteSpace(Cookies))
                {
                    SetCookies(Cookies);
                }

                // 设置POST数据
                if (Method == HttpMethod.Post)
                {
                    request.Content = new StringContent(PostData, Encode, ContentType);
                }

                // 发送请求
                response = await _httpClient.SendAsync(request);

                // 检查响应状态
                response.EnsureSuccessStatusCode();

                // 读取响应内容
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(responseStream, Encode))
                {
                    result.Html = await reader.ReadToEndAsync();
                }

                // 更新Cookies
                Cookies = GetCookies();

                // 设置结果
                result.Response = response;
                result.StatusCode = response.StatusCode;

                return result;
            }
            catch (HttpRequestException httpEx)
            {
                lastException = httpEx;
                attemptCount++;

                if (attemptCount > MaxRetries)
                {
                    result.StatusCode = response?.StatusCode ?? HttpStatusCode.BadRequest;
                    result.ErrorMessage = $"HTTP请求失败，已重试{MaxRetries}次。错误: {httpEx.Message}";
                    return result;
                }
            }
            catch (TaskCanceledException tcEx)
            {
                lastException = tcEx;
                attemptCount++;

                if (attemptCount > MaxRetries)
                {
                    result.StatusCode = HttpStatusCode.RequestTimeout;
                    result.ErrorMessage = $"请求超时，已重试{MaxRetries}次";
                    return result;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                attemptCount++;

                if (attemptCount > MaxRetries)
                {
                    result.StatusCode = HttpStatusCode.BadRequest;
                    result.ErrorMessage = $"请求发生错误，已重试{MaxRetries}次。错误: {ex.Message}";
                    return result;
                }
            }
            finally
            {
                // 释放请求对象（响应对象需要保留供外部使用）
                request?.Dispose();
            }

            // 重试前等待
            if (attemptCount <= MaxRetries)
            {
                await Task.Delay(RetryDelay);
            }
        }

        return result;
    }

    /// <summary>
    /// 解析Cookie字符串并添加到Cookie容器
    /// 支持标准的Cookie格式：name=value; name2=value2
    /// </summary>
    /// <param name="cookieString">Cookie字符串</param>
    public void SetCookies(string cookieString)
    {
        if (string.IsNullOrWhiteSpace(cookieString))
            return;

        if (string.IsNullOrWhiteSpace(Url))
            throw new InvalidOperationException("设置Cookie前必须先设置URL");

        try
        {
            var uri = new Uri(Url);

            // 分割Cookie字符串
            string[] cookies = cookieString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var cookie in cookies)
            {
                var trimmedCookie = cookie.Trim();
                if (string.IsNullOrWhiteSpace(trimmedCookie))
                    continue;

                // 分割名称和值（只分割第一个=号）
                int equalIndex = trimmedCookie.IndexOf('=');
                if (equalIndex > 0 && equalIndex < trimmedCookie.Length - 1)
                {
                    string name = trimmedCookie.Substring(0, equalIndex).Trim();
                    string value = trimmedCookie.Substring(equalIndex + 1).Trim();

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        try
                        {
                            _cookieContainer.Add(uri, new Cookie(name, value));
                        }
                        catch (CookieException)
                        {
                            // 忽略无效的Cookie，继续处理其他Cookie
                        }
                    }
                }
            }
        }
        catch (UriFormatException)
        {
            throw new ArgumentException($"URL格式不正确: {Url}");
        }
    }

    /// <summary>
    /// 获取当前URL对应的所有Cookie
    /// </summary>
    /// <returns>Cookie字符串，格式为name=value; name2=value2</returns>
    public string GetCookies()
    {
        if (string.IsNullOrWhiteSpace(Url))
            return string.Empty;

        try
        {
            var uri = new Uri(Url);
            return _cookieContainer.GetCookieHeader(uri);
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

}
