# DZLUtility

一个包含各种不依赖其他类的工具的项目。

## 类库使用说明

### AliyunOcrClient

**功能:** 用于调用阿里云OCR服务，识别图片中的文字。

**使用方法:**

```csharp
var client = new AliyunOcrClient("your_appcode");
var result = await client.RecognizeTextAsync("image_url");
Console.WriteLine(result);
```

### EasyHttpClient

**功能:** 一个简单的HTTP客户端，用于发送GET和POST请求。

**使用方法:**

```csharp
var client = new EasyHttpClient();

// 发送GET请求
var getData = await client.GetAsync("https://api.example.com/data");
Console.WriteLine(getData);

// 发送POST请求
var postData = new { key = "value" };
var postResult = await client.PostAsync("https://api.example.com/submit", postData);
Console.WriteLine(postResult);
```

### HttpRequestPool

**功能:** 一个`HttpRequestMessage`对象池，用于管理和重用`HttpRequestMessage`对象，以提高性能和减少资源消耗。

**使用方法:**

```csharp
var pool = new HttpRequestPool();

// 从池中获取一个HttpRequestMessage对象
var request = pool.Get();
request.RequestUri = new Uri("https://api.example.com/data");
request.Method = HttpMethod.Get;

// 使用request...

// 将request对象返回池中
pool.Return(request);
```

### MiniMaxT2AClient

**功能:** 用于调用MiniMax的文本转语音（T2A）API，将文本转换为语音。

**使用方法:**

```csharp
var client = new MiniMaxT2AClient("your_group_id", "your_api_key");
var response = await client.GetT2A("要转换的文本");
// 处理响应...
```

### SiliconFlowClient

**功能:** 用于调用SiliconFlow的文本转图像API，根据文本描述生成图像。

**使用方法:**

```csharp
var client = new SiliconFlowClient("your_api_key");
var imageBytes = await client.TextToImageAsync("a cute cat");
// 处理图片...
```

### WindowEnumerator

**功能:** 枚举当前打开的所有窗口，并提供按标题或类名查找窗口的功能。

**使用方法:**

```csharp
// 获取所有窗口
var allWindows = WindowEnumerator.GetOpenWindows();
foreach (var window in allWindows)
{
    Console.WriteLine($"Window Title: {window.Value}, HWND: {window.Key}");
}

// 按标题查找窗口
var notepadWindow = WindowEnumerator.FindWindowByTitle("Notepad");
if (notepadWindow != IntPtr.Zero)
{
    Console.WriteLine("Notepad is open.");
}
```
