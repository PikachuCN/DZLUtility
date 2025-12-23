//
// 您可以【直接复制】此代码替换掉之前有问题的版本。
// 核心修正点在于 PercentEncode 方法的实现，使其更严格地遵循RFC3986。
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

#region 数据模型 (Data Models)
// 这些类用于将API返回的JSON数据映射为强类型的C#对象

/// <summary>
/// 发票商品详情
/// </summary>
public class InvoiceDetail
{
    /// <summary>
    /// 商品名称
    /// </summary>
    [JsonPropertyName("itemName")]
    public string ItemName { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    [JsonPropertyName("quantity")]
    public string Quantity { get; set; }

    /// <summary>
    /// 单价
    /// </summary>
    [JsonPropertyName("unitPrice")]
    public string UnitPrice { get; set; }

    /// <summary>
    /// 金额
    /// </summary>
    [JsonPropertyName("amount")]
    public string Amount { get; set; }

    /// <summary>
    /// 税率
    /// </summary>
    [JsonPropertyName("taxRate")]
    public string TaxRate { get; set; }

    /// <summary>
    /// 税额
    /// </summary>
    [JsonPropertyName("tax")]
    public string Tax { get; set; }
}

/// <summary>
/// 结构化的发票核心数据
/// </summary>
public class InvoiceData
{
    /// <summary>
    /// 发票号码
    /// </summary>
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; set; }

    /// <summary>
    /// 开票日期
    /// </summary>
    [JsonPropertyName("invoiceDate")]
    public string InvoiceDate { get; set; }

    /// <summary>
    /// 购买方名称
    /// </summary>
    [JsonPropertyName("purchaserName")]
    public string PurchaserName { get; set; }

    /// <summary>
    /// 购买方纳税人识别号
    /// </summary>
    [JsonPropertyName("purchaserTaxNumber")]
    public string PurchaserTaxNumber { get; set; }

    /// <summary>
    /// 销售方名称
    /// </summary>
    [JsonPropertyName("sellerName")]
    public string SellerName { get; set; }

    /// <summary>
    /// 销售方纳税人识别号
    /// </summary>
    [JsonPropertyName("sellerTaxNumber")]
    public string SellerTaxNumber { get; set; }

    /// <summary>
    /// 价税合计(小写)
    /// </summary>
    [JsonPropertyName("invoiceAmountPreTax")]
    public string InvoiceAmountPreTax { get; set; }

    /// <summary>
    /// 合计税额
    /// </summary>
    [JsonPropertyName("invoiceTax")]
    public string InvoiceTax { get; set; }

    /// <summary>
    /// 价税合计(大写)
    /// </summary>
    [JsonPropertyName("totalAmount")]
    public string TotalAmount { get; set; }

    /// <summary>
    /// 价税合计(小写)
    /// </summary>
    [JsonPropertyName("totalAmountInWords")]
    public string TotalAmountInWords { get; set; }

    /// <summary>
    /// 开票人
    /// </summary>
    [JsonPropertyName("drawer")]
    public string Drawer { get; set; }

    /// <summary>
    /// 发票类型
    /// </summary>
    [JsonPropertyName("invoiceType")]
    public string InvoiceType { get; set; }

    /// <summary>
    /// 发票商品详情列表
    /// </summary>
    [JsonPropertyName("invoiceDetails")]
    public List<InvoiceDetail> InvoiceDetails { get; set; }
}

/// <summary>
/// API返回的最外层JSON结构
/// </summary>
internal class OcrTopLevelResponse
{
    /// <summary>
    /// 请求ID
    /// </summary>
    [JsonPropertyName("RequestId")]
    public string RequestId { get; set; }

    // 阿里云的Data字段是一个内嵌了JSON的字符串，需要二次解析
    /// <summary>
    /// 内嵌的JSON字符串
    /// </summary>
    [JsonPropertyName("Data")]
    public string InnerDataString { get; set; }
}

/// <summary>
/// 内层Data字符串解析后的结构
/// </summary>
internal class OcrInnerData
{
    /// <summary>
    /// 最终的发票数据
    /// </summary>
    [JsonPropertyName("data")]
    public InvoiceData Data { get; set; }
}

#endregion


/// <summary>
/// 阿里云OCR API的独立客户端，无需官方SDK。
/// 此客户端封装了调用阿里云增值税发票识别API所需的所有逻辑，包括签名计算和HTTP请求。
/// 支持单个API Key或多个API Key轮询使用。
/// </summary>
public class AliyunOcrClient
{
    /// <summary>
    /// 内部类：存储一组API凭证
    /// </summary>
    private class KeyCredential
    {
        public string AccessKeyId { get; }
        public string AccessKeySecret { get; }

        public KeyCredential(string accessKeyId, string accessKeySecret)
        {
            AccessKeyId = accessKeyId;
            AccessKeySecret = accessKeySecret;
        }
    }

    private readonly List<KeyCredential> _keyCredentials;
    private readonly string _endpoint;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private int _currentKeyIndex = 0;
    private readonly object _keyLock = new object();

    /// <summary>
    /// 获取当前配置的API Key数量。
    /// </summary>
    public int KeyCount => _keyCredentials.Count;

    /// <summary>
    /// 初始化阿里云OCR客户端（单个API Key）。
    /// </summary>
    /// <param name="accessKeyId">您的阿里云AccessKey ID。</param>
    /// <param name="accessKeySecret">您的阿里云AccessKey Secret。</param>
    /// <param name="endpoint">API接入点，默认为杭州地域。</param>
    public AliyunOcrClient(string accessKeyId, string accessKeySecret, string endpoint = "ocr-api.cn-hangzhou.aliyuncs.com")
        : this(new[] { (accessKeyId, accessKeySecret) }, endpoint)
    {
    }

    /// <summary>
    /// 初始化阿里云OCR客户端（多个API Key轮询使用）。
    /// </summary>
    /// <param name="credentials">API凭证数组，每个元素为 (AccessKeyId, AccessKeySecret) 元组。</param>
    /// <param name="endpoint">API接入点，默认为杭州地域。</param>
    /// <exception cref="ArgumentException">当凭证数组为空时抛出。</exception>
    public AliyunOcrClient((string accessKeyId, string accessKeySecret)[] credentials, string endpoint = "ocr-api.cn-hangzhou.aliyuncs.com")
    {
        if (credentials == null || credentials.Length == 0)
            throw new ArgumentException("API凭证数组不能为空", nameof(credentials));

        _keyCredentials = new List<KeyCredential>();
        foreach (var (accessKeyId, accessKeySecret) in credentials)
        {
            _keyCredentials.Add(new KeyCredential(accessKeyId, accessKeySecret));
        }

        _endpoint = endpoint.StartsWith("https://") ? endpoint : "https://" + endpoint;
        _httpClient = new HttpClient();

        // 配置JSON序列化选项，以便在反序列化时忽略属性名称的大小写差异
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
    }

    /// <summary>
    /// 获取下一组API凭证（线程安全的轮询）。
    /// </summary>
    /// <returns>下一组要使用的API凭证。</returns>
    private KeyCredential GetNextCredentials()
    {
        lock (_keyLock)
        {
            var credential = _keyCredentials[_currentKeyIndex];
            _currentKeyIndex = (_currentKeyIndex + 1) % _keyCredentials.Count;
            return credential;
        }
    }

    /// <summary>
    /// 异步调用阿里云增值税发票识别接口。
    /// </summary>
    /// <param name="imageStream">包含发票图像数据的文件流。</param>
    /// <returns>一个 <see cref="Task{InvoiceData}"/>，表示异步操作。
    /// 任务结果是包含已识别发票信息的 <see cref="InvoiceData"/> 对象。
    /// 如果识别失败或API返回错误，则可能返回null或抛出异常。</returns>
    public async Task<InvoiceData> RecognizeInvoiceAsync(Stream imageStream)
    {
        if (imageStream == null) throw new ArgumentNullException(nameof(imageStream));

        // 获取下一组凭证（轮询）
        var credential = GetNextCredentials();

        // 1. 准备通用请求参数
        var parameters = new Dictionary<string, string>
        {
            { "Format", "JSON" }, // 返回格式为JSON
            { "Version", "2021-07-07" }, // API版本号
            { "AccessKeyId", credential.AccessKeyId }, // 访问密钥ID
            { "SignatureMethod", "HMAC-SHA1" }, // 签名方法
            { "Timestamp", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") }, // UTC时间戳
            { "SignatureVersion", "1.0" }, // 签名版本
            { "SignatureNonce", Guid.NewGuid().ToString() }, // 随机数，防止重放攻击
            { "Action", "RecognizeInvoice" } // 要执行的操作
        };

        // 2. 计算签名
        string signature = Sign(parameters, credential.AccessKeySecret);
        parameters.Add("Signature", signature);

        // 3. 构建完整的请求URL
        string queryString = string.Join("&", parameters.Select(kvp => $"{PercentEncode(kvp.Key)}={PercentEncode(kvp.Value)}"));
        string requestUrl = $"{_endpoint}/?{queryString}";

        // 4. 创建并发送HTTP POST请求
        using (var requestMessage = new HttpRequestMessage(HttpMethod.Post, requestUrl))
        {
            // 将图片流作为请求体
            requestMessage.Content = new StreamContent(imageStream);
            requestMessage.Content.Headers.Add("Content-Type", "application/octet-stream");

            try
            {
                // 发送请求并获取响应
                HttpResponseMessage response = await _httpClient.SendAsync(requestMessage);
                string responseBody = await response.Content.ReadAsStringAsync();

                // 检查响应状态码，如果不是成功状态，则抛出异常
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"请求失败，状态码: {response.StatusCode}, 响应内容: {responseBody}");
                }

                // 5. 解析第一层JSON响应
                var topLevelResponse = JsonSerializer.Deserialize<OcrTopLevelResponse>(responseBody, _jsonOptions);
                if (string.IsNullOrEmpty(topLevelResponse?.InnerDataString))
                {
                    // 如果内层的Data字段为空字符串，说明API可能未能成功识别或返回了错误信息
                    Console.WriteLine("警告: 阿里云OCR API返回的内层Data为空，可能是图片质量问题或无法识别。");
                    return null;
                }

                // 6. 解析内嵌在Data字段中的第二层JSON
                var innerData = JsonSerializer.Deserialize<OcrInnerData>(topLevelResponse.InnerDataString, _jsonOptions);

                // 7. 返回最终提取出的结构化发票数据
                return innerData?.Data;
            }
            catch (JsonException jsonEx)
            {
                // 如果JSON解析失败，则抛出更具体的异常信息
                throw new Exception($"解析阿里云OCR API响应时发生JSON错误: {jsonEx.Message}", jsonEx);
            }
            catch (Exception ex)
            {
                // 捕获其他所有异常（如网络问题）
                throw new Exception($"调用阿里云OCR API或处理响应时发生未知错误: {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// 为API请求计算HMAC-SHA1签名。
    /// </summary>
    /// <param name="parameters">所有请求参数的字典（不包括Signature本身）。</param>
    /// <param name="accessKeySecret">用于签名的AccessKey Secret。</param>
    /// <returns>Base64编码的签名字符串。</returns>
    private string Sign(Dictionary<string, string> parameters, string accessKeySecret)
    {
        // 1. 将参数按Key进行字典序排序
        var sortedParams = parameters.OrderBy(kvp => kvp.Key, StringComparer.Ordinal);
        
        // 2. 构建规范化的查询字符串
        var canonicalizedQueryString = new StringBuilder();
        foreach (var kvp in sortedParams)
        {
            canonicalizedQueryString.Append('&')
                .Append(PercentEncode(kvp.Key)).Append('=')
                .Append(PercentEncode(kvp.Value));
        }

        // 3. 构建待签名的字符串 (String-to-Sign)
        var stringToSign = new StringBuilder();
        stringToSign.Append("POST"); // HTTP方法
        stringToSign.Append('&');
        stringToSign.Append(PercentEncode("/")); // 资源路径
        stringToSign.Append('&');
        // 移除开头的'&'后进行编码
        stringToSign.Append(PercentEncode(canonicalizedQueryString.ToString().Substring(1)));

        // 4. 计算HMAC-SHA1签名
        // 密钥是 AccessKeySecret + "&"
        using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(accessKeySecret + "&")))
        {
            byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign.ToString()));
            // 将签名结果转换为Base64字符串
            return Convert.ToBase64String(hashBytes);
        }
    }

    //
    // ============================【重要修正】============================
    // 下面的 PercentEncode 方法已被重写，以确保所有特殊字符都被正确编码。
    //
    /// <summary>
    /// 对字符串进行符合RFC3986规范的URL编码。
    /// 阿里云要求对参数名和参数值进行此种编码。
    /// </summary>
    /// <param name="value">要编码的原始字符串。</param>
    /// <returns>编码后的字符串。</returns>
    private string PercentEncode(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var stringBuilder = new StringBuilder();
        // 根据RFC3986，这些字符是"非保留字符"，在URL编码中无需转义
        const string unreservedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";

        foreach (char symbol in value)
        {
            if (unreservedChars.IndexOf(symbol) != -1)
            {
                // 如果是无需转义的字符，直接附加
                stringBuilder.Append(symbol);
            }
            else
            {
                // 对其他所有字符（保留字符和多字节字符）进行UTF-8编码，
                // 然后将每个字节转换为%XX的十六进制格式
                byte[] bytes = Encoding.UTF8.GetBytes(new char[] { symbol });
                foreach (byte b in bytes)
                {
                    stringBuilder.AppendFormat("%{0:X2}", b);
                }
            }
        }

        return stringBuilder.ToString();
    }
}