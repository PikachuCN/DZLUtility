using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


    /// <summary>
    /// HTTP请求池，提供并发控制和任务管理功能
    /// 支持最大并发数限制、任务队列、进度跟踪和回调机制
    /// </summary>
    public class HttpRequestPool : IDisposable
    {
        /// <summary>
        /// HTTP请求任务的封装类
        /// </summary>
        public class HttpRequestTask
        {
            /// <summary>
            /// 任务唯一标识符
            /// </summary>
            public string TaskId { get; }
            
            /// <summary>
            /// 任务名称或描述
            /// </summary>
            public string TaskName { get; set; }
            
            /// <summary>
            /// HTTP客户端实例
            /// </summary>
            public EasyHttpClient HttpClient { get; set; }
            
            /// <summary>
            /// 请求URL
            /// </summary>
            public string Url { get; set; }
            
            /// <summary>
            /// POST数据（如果是POST请求）
            /// </summary>
            public string PostData { get; set; }
            
            /// <summary>
            /// 请求方法（GET或POST）
            /// </summary>
            public System.Net.Http.HttpMethod Method { get; set; }
            
            /// <summary>
            /// 任务创建时间
            /// </summary>
            public DateTime CreateTime { get; }
            
            /// <summary>
            /// 任务开始执行时间
            /// </summary>
            public DateTime? StartTime { get; set; }
            
            /// <summary>
            /// 任务完成时间
            /// </summary>
            public DateTime? CompleteTime { get; set; }
            
            /// <summary>
            /// 任务状态
            /// </summary>
            public TaskStatus Status { get; set; }
            
            /// <summary>
            /// 执行结果
            /// </summary>
            public EasyHttpClient.HttpResultData Result { get; set; }
            
            /// <summary>
            /// 任务完成回调函数
            /// </summary>
            public Action<HttpRequestTask> OnComplete { get; set; }
            
            /// <summary>
            /// 任务失败回调函数
            /// </summary>
            public Action<HttpRequestTask, Exception> OnError { get; set; }
            
            /// <summary>
            /// 构造函数
            /// </summary>
            public HttpRequestTask()
            {
                TaskId = Guid.NewGuid().ToString();
                CreateTime = DateTime.Now;
                Status = TaskStatus.Pending;
                Method = System.Net.Http.HttpMethod.Get;
            }
        }
        
        /// <summary>
        /// 任务状态枚举
        /// </summary>
        public enum TaskStatus
        {
            /// <summary>
            /// 等待执行
            /// </summary>
            Pending,
            
            /// <summary>
            /// 正在执行
            /// </summary>
            Running,
            
            /// <summary>
            /// 执行完成
            /// </summary>
            Completed,
            
            /// <summary>
            /// 执行失败
            /// </summary>
            Failed,
            
            /// <summary>
            /// 已取消
            /// </summary>
            Cancelled
        }
        
        /// <summary>
        /// 请求池状态信息
        /// </summary>
        public class PoolStatus
        {
            /// <summary>
            /// 总任务数
            /// </summary>
            public int TotalTasks { get; set; }
            
            /// <summary>
            /// 等待中的任务数
            /// </summary>
            public int PendingTasks { get; set; }
            
            /// <summary>
            /// 正在执行的任务数
            /// </summary>
            public int RunningTasks { get; set; }
            
            /// <summary>
            /// 已完成的任务数
            /// </summary>
            public int CompletedTasks { get; set; }
            
            /// <summary>
            /// 失败的任务数
            /// </summary>
            public int FailedTasks { get; set; }
            
            /// <summary>
            /// 已取消的任务数
            /// </summary>
            public int CancelledTasks { get; set; }
            
            /// <summary>
            /// 池是否正在运行
            /// </summary>
            public bool IsRunning { get; set; }
        }
        
        // 私有字段
        private readonly int _maxConcurrency;
        private readonly SemaphoreSlim _semaphore;
        private readonly ConcurrentQueue<HttpRequestTask> _taskQueue;
        private readonly ConcurrentDictionary<string, HttpRequestTask> _allTasks;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private Task _processTask;
        private bool _isDisposed;
        
        // 统计字段
        private int _totalTasks;
        private int _runningTasks;
        private int _completedTasks;
        private int _failedTasks;
        private int _cancelledTasks;
        
        /// <summary>
        /// 获取最大并发数
        /// </summary>
        public int MaxConcurrency => _maxConcurrency;
        
        /// <summary>
        /// 获取当前是否正在运行
        /// </summary>
        public bool IsRunning { get; private set; }
        
        /// <summary>
        /// 所有任务完成时的回调
        /// </summary>
        public event Action<PoolStatus> OnAllTasksCompleted;
        
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="maxConcurrency">最大并发数</param>
        public HttpRequestPool(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentException("最大并发数必须大于0", nameof(maxConcurrency));
                
            _maxConcurrency = maxConcurrency;
            _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            _taskQueue = new ConcurrentQueue<HttpRequestTask>();
            _allTasks = new ConcurrentDictionary<string, HttpRequestTask>();
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        /// <summary>
        /// 添加单个任务（同步方法）
        /// </summary>
        /// <param name="task">要添加的任务</param>
        public void AddTask(HttpRequestTask task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));
                
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(HttpRequestPool));
                
            // 验证任务
            if (string.IsNullOrWhiteSpace(task.Url))
                throw new ArgumentException("任务URL不能为空");
                
            if (task.HttpClient == null)
                task.HttpClient = new EasyHttpClient();
                
            // 添加到集合
            if (_allTasks.TryAdd(task.TaskId, task))
            {
                _taskQueue.Enqueue(task);
                Interlocked.Increment(ref _totalTasks);
                
                // 如果还没有启动处理任务，则启动
                EnsureProcessingStarted();
            }
        }
        
        /// <summary>
        /// 批量添加任务（同步方法）
        /// </summary>
        /// <param name="tasks">要添加的任务列表</param>
        public void AddTasks(IEnumerable<HttpRequestTask> tasks)
        {
            if (tasks == null)
                throw new ArgumentNullException(nameof(tasks));
                
            foreach (var task in tasks)
            {
                AddTask(task);
            }
        }
        
        /// <summary>
        /// 创建并添加GET请求任务
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="onComplete">完成回调</param>
        /// <param name="onError">错误回调</param>
        /// <returns>任务ID</returns>
        public string AddGetRequest(string url, 
            Action<HttpRequestTask> onComplete = null, 
            Action<HttpRequestTask, Exception> onError = null)
        {
            var task = new HttpRequestTask
            {
                Url = url,
                Method = System.Net.Http.HttpMethod.Get,
                OnComplete = onComplete,
                OnError = onError
            };
            
            AddTask(task);
            return task.TaskId;
        }
        
        /// <summary>
        /// 创建并添加POST请求任务
        /// </summary>
        /// <param name="url">请求URL</param>
        /// <param name="postData">POST数据</param>
        /// <param name="onComplete">完成回调</param>
        /// <param name="onError">错误回调</param>
        /// <returns>任务ID</returns>
        public string AddPostRequest(string url, string postData,
            Action<HttpRequestTask> onComplete = null, 
            Action<HttpRequestTask, Exception> onError = null)
        {
            var task = new HttpRequestTask
            {
                Url = url,
                PostData = postData,
                Method = System.Net.Http.HttpMethod.Post,
                OnComplete = onComplete,
                OnError = onError
            };
            
            AddTask(task);
            return task.TaskId;
        }
        
        /// <summary>
        /// 获取当前池状态
        /// </summary>
        /// <returns>池状态信息</returns>
        public PoolStatus GetStatus()
        {
            return new PoolStatus
            {
                TotalTasks = _totalTasks,
                PendingTasks = _taskQueue.Count,
                RunningTasks = _runningTasks,
                CompletedTasks = _completedTasks,
                FailedTasks = _failedTasks,
                CancelledTasks = _cancelledTasks,
                IsRunning = IsRunning
            };
        }
        
        /// <summary>
        /// 获取指定任务的状态
        /// </summary>
        /// <param name="taskId">任务ID</param>
        /// <returns>任务对象，如果不存在则返回null</returns>
        public HttpRequestTask GetTask(string taskId)
        {
            _allTasks.TryGetValue(taskId, out var task);
            return task;
        }
        
        /// <summary>
        /// 获取所有任务列表
        /// </summary>
        /// <returns>任务列表</returns>
        public List<HttpRequestTask> GetAllTasks()
        {
            return _allTasks.Values.ToList();
        }
        
        /// <summary>
        /// 等待所有任务完成
        /// </summary>
        /// <param name="timeout">超时时间，null表示无限等待</param>
        /// <returns>是否在超时前完成</returns>
        public async Task<bool> WaitAllTasksAsync(TimeSpan? timeout = null)
        {
            var startTime = DateTime.Now;
            
            while (IsRunning || _taskQueue.Count > 0 || _runningTasks > 0)
            {
                if (timeout.HasValue && DateTime.Now - startTime > timeout.Value)
                    return false;
                    
                await Task.Delay(100);
            }
            
            return true;
        }
        
        /// <summary>
        /// 停止接收新任务并等待现有任务完成
        /// </summary>
        public async Task StopAsync()
        {
            IsRunning = false;
            
            // 等待所有任务完成
            await WaitAllTasksAsync();
            
            // 取消令牌
            _cancellationTokenSource.Cancel();
            
            // 等待处理任务结束
            if (_processTask != null)
            {
                await _processTask;
            }
        }
        
        /// <summary>
        /// 立即停止所有任务
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
            _cancellationTokenSource.Cancel();
            
            // 将所有等待中的任务标记为已取消
            while (_taskQueue.TryDequeue(out var task))
            {
                task.Status = TaskStatus.Cancelled;
                Interlocked.Increment(ref _cancelledTasks);
            }
        }
        
        /// <summary>
        /// 确保处理任务已启动
        /// </summary>
        private void EnsureProcessingStarted()
        {
            lock (_lockObject)
            {
                if (!IsRunning && !_isDisposed)
                {
                    IsRunning = true;
                    _processTask = Task.Run(() => ProcessTasksAsync(_cancellationTokenSource.Token));
                }
            }
        }
        
        /// <summary>
        /// 处理任务队列的核心方法
        /// </summary>
        private async Task ProcessTasksAsync(CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            
            while (!cancellationToken.IsCancellationRequested)
            {
                // 如果没有待处理任务，稍等一下
                if (_taskQueue.IsEmpty)
                {
                    if (_runningTasks == 0)
                    {
                        // 所有任务都完成了
                        OnAllTasksCompleted?.Invoke(GetStatus());
                        IsRunning = false;
                        break;
                    }
                    
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                
                // 等待信号量
                await _semaphore.WaitAsync(cancellationToken);
                
                // 尝试获取任务
                if (_taskQueue.TryDequeue(out var task))
                {
                    // 启动任务
                    var executionTask = Task.Run(async () =>
                    {
                        try
                        {
                            await ExecuteTaskAsync(task);
                        }
                        finally
                        {
                            _semaphore.Release();
                        }
                    }, cancellationToken);
                    
                    tasks.Add(executionTask);
                    
                    // 清理已完成的任务
                    tasks.RemoveAll(t => t.IsCompleted);
                }
                else
                {
                    // 没有获取到任务，释放信号量
                    _semaphore.Release();
                }
            }
            
            // 等待所有任务完成
            await Task.WhenAll(tasks);
        }
        
        /// <summary>
        /// 执行单个任务
        /// </summary>
        private async Task ExecuteTaskAsync(HttpRequestTask task)
        {
            Interlocked.Increment(ref _runningTasks);
            task.Status = TaskStatus.Running;
            task.StartTime = DateTime.Now;
            
            try
            {
                // 设置HTTP客户端参数
                task.HttpClient.Url = task.Url;
                task.HttpClient.Method = task.Method;
                
                if (task.Method == System.Net.Http.HttpMethod.Post)
                {
                    task.HttpClient.PostData = task.PostData;
                }
                
                // 执行请求
                task.Result = await task.HttpClient.Send();
                
                // 标记为完成
                task.Status = TaskStatus.Completed;
                task.CompleteTime = DateTime.Now;
                Interlocked.Increment(ref _completedTasks);
                
                // 调用完成回调
                task.OnComplete?.Invoke(task);
            }
            catch (Exception ex)
            {
                // 标记为失败
                task.Status = TaskStatus.Failed;
                task.CompleteTime = DateTime.Now;
                Interlocked.Increment(ref _failedTasks);
                
                // 设置错误信息
                if (task.Result == null)
                {
                    task.Result = new EasyHttpClient.HttpResultData(task.HttpClient)
                    {
                        ErrorMessage = ex.Message
                    };
                }
                
                // 调用错误回调
                task.OnError?.Invoke(task, ex);
            }
            finally
            {
                Interlocked.Decrement(ref _runningTasks);
            }
        }
        
        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;
                
            _isDisposed = true;
            
            Stop();
            
            _semaphore?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
    }
