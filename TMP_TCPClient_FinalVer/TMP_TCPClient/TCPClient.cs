using System;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Threading.Timer;

namespace TMP_TCPClient
{
    /// <summary>
    /// TCP客户端模板
    /// </summary>
    public class TCPClient
    {
        #region 变量
        #region TCP变量
        /// <summary>
        /// 客户端端口号
        /// </summary>
        protected int port { get; set; }   
        /// <summary>
        /// 客户端IP4
        /// </summary>
        protected string IPV4 { get; set; }
        /// <summary>
        /// 官方客户端封装的套接字实例
        /// </summary>
        protected TcpClient? tcpClient;
        /// <summary>
        /// 网络流
        /// </summary>
        protected NetworkStream? stream { get; set; }
        private Timer? heartbeattimer = null;
        #endregion
        #region 数据存储容器
        /// <summary>
        /// 存储异步接收的字符串的安全消息队列
        /// </summary>
        protected ConcurrentQueue<string>? RecvMsgQueue { get; set; }
        /// <summary>
        /// 存储发送字符串的安全消息队列
        /// </summary>
        protected ConcurrentQueue<string>? SendMsgQueue { get; set; }
        /// <summary>
        /// 存储需要进行异步CPU密集型计算的消息队列
        /// </summary>
        protected ConcurrentQueue<string>? NeedAnalysisByCPUAsync_RecvMsgQeueue {  get; set; }
        #endregion
        #endregion

        #region 工具包（有些含锁）
        #region 取消令牌包
        /// <summary>
        /// 异步自动接收取消令牌
        /// </summary>
        protected CancellationTokenSource? AutoRecvMsgcts;
        /// <summary>
        /// 异步自动更新UI和分析接收的数据取消令牌
        /// </summary>
        protected CancellationTokenSource? InvokeAndAnalysisRecvDatacts;
        /// <summary>
        /// 异步心跳包机制取消令牌
        /// </summary>
        protected CancellationTokenSource? HeartbeatCheckcts;
        /// <summary>
        /// 异步自动发送消息取消令牌
        /// </summary>
        protected CancellationTokenSource? AutoSendMsgcts;
        #endregion
        #region 网络流流式传输工具包

        #endregion
        #region 加锁数据包
        #region 布尔加锁包
        /// <summary>
        /// 布尔加锁包
        /// </summary>
        private readonly object _TCPClientDisConnectedLock = new object();
        private readonly object _IsConnectingLock = new object();
        #region 客户端与服务端是否已断开连接
        private volatile bool _TcpClientDisconnected = true;
        public bool TcpClientDisConnected
        {
            get { lock (_TCPClientDisConnectedLock) return _TcpClientDisconnected; }
            set { lock (_TCPClientDisConnectedLock) _TcpClientDisconnected = value; }
        }
        #endregion
        #region 客户端是否正在连接
        private volatile bool _isConnecting = false;
        /// <summary>
        /// 客户端是否正在连接
        /// </summary>
        public bool IsConnecting
        {
            get { lock (_IsConnectingLock) return _isConnecting; }
            set { lock (_IsConnectingLock) _isConnecting = value; }
        }
        #endregion
        #endregion
        #endregion
        #region 安全启动异步任务工具包
        /// <summary>
        /// 后台异步任务更新锁
        /// </summary>
        private readonly object _BackgroundTasklock = new object();
        /// <summary>
        /// 后台异步任务列表
        /// </summary>
        private readonly List<Task> BackgroundTaskList = new List<Task>();
        /// <summary>
        /// 读取异步锁，防止多个读取任务同时读取一块字节
        /// </summary>
        private readonly SemaphoreSlim RecvAsyncLock = new SemaphoreSlim(1, 1);
        #endregion
        #region 事件委托包
        public event Action<string>? InvokeUI;      ///更新UI事件委托
        #endregion
        #region 固定常量包（包含心跳包、日志包）
        ///<summary>
        ///接收字节大小常量（默认8Kb）
        ///</summary>
        protected readonly long RecvbufferSize = 1024 * 8;
        #region 日志常量包
        /// <summary>
        /// TCP记录日志路径
        /// </summary>
        private readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Log.txt");
        /// <summary>
        /// TCP异常日志
        /// </summary>
        private readonly string _exceptionLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExLog.txt");
        /// <summary>
        /// 文件写入锁
        /// </summary>
        private readonly object _fileLock = new object();
        /// <summary>
        /// 日志文件大小阈值（4MB）
        /// </summary>
        private readonly long _logFileSizeThreshold = 4 * 1024 * 1024;
        #endregion
        #region 心跳常量包
        /// <summary>
        /// 心跳检测标识字符串
        /// </summary>
        private readonly string _HeartBeatStrSignal = "HeartBeat";
        /// <summary>
        /// 心跳发送间隔（毫秒）
        /// </summary>
        private readonly int _heartBeatIntervalMS = 3000;
        /// <summary>
        /// 心跳失败超时阈值（连续失败次数）
        /// </summary>
        private readonly int _heartBeatTimeoutThreshold = 3;
        /// <summary>
        /// 当前心跳失败计数
        /// </summary>
        private volatile int _heartBeatFailCount = 0;
        /// <summary>
        /// 加锁后当前心跳失败计数
        /// </summary>
        private int HeartBeatFailCount
        {
            get { lock (_heartBeatCountLock) return _heartBeatFailCount; }
            set { lock (_heartBeatCountLock) _heartBeatFailCount = value; }
        }
        /// <summary>
        /// 心跳计数锁
        /// </summary>
        private readonly object _heartBeatCountLock = new object();
        /// <summary>
        /// 心跳任务内存锁
        /// </summary>
        private readonly object _timerCallbackLock = new object();
        #endregion
        #endregion
        #region 枚举包
        /// <summary>
        /// 默认数据分析模式枚举
        /// </summary>
        public enum DefaultAnalysisType 
        {
            /// <summary>
            /// 无模式
            /// </summary>
            None = 0,
            /// <summary>
            /// 异步IO拆包、分类并赋值
            /// </summary>
            Async_Unpack_ClassifyAndAssign = 1,
            /// <summary>
            /// CPU异步计算
            /// </summary>
            Async_CPU_Calculation = 2
        }

        /// <summary>
        /// 默认封包模式
        /// </summary>
        public enum DefaultWrapMode
        {
            /// <summary>
            /// 无封包（默认）
            /// </summary>
            None = 0,

            /// <summary>
            /// 加入大端序封包
            /// </summary>
            AddBigEndian = 1,


        }

        /// <summary>
        /// 数据分析模式枚举拓展接口
        /// </summary>
        public virtual Type AnalysisType => typeof(DefaultAnalysisType);

        /// <summary>
        /// 封包模式枚举拓展接口
        /// </summary>
        public virtual Type WrapMode => typeof(DefaultWrapMode);
        #endregion
        #endregion

        #region 构造
        /// <summary>
        /// 构造
        /// </summary>
        /// <param name="_port">端口</param>
        /// <param name="_IPV4">IP4</param>
        public TCPClient(int _port,string _IPV4) 
        {
            port = _port;
            IPV4 = _IPV4;
        }
        #endregion


        #region 基类TCP基本异步IO方法（发送、接收、心跳机制），供在WinForm和WPF项目中的子类快速使用
        #region 异步连接
        /// <summary>
        /// 等待式(await)异步连接
        /// </summary>
        /// <param name="ConnectTimeOutMS">超时阈值</param>
        /// <returns>连接是否成功</returns>
        protected async Task<bool> ConnectAsync(int ConnectTimeOutMS = 3000)
        {
            #region TCP异步连接前的检查
            ///小于阈值强制默认赋值
            if(ConnectTimeOutMS <= 500)
            {
                ConnectTimeOutMS = 3000;
            }
            ///检查IP是否合规
            if (!IPAddress.TryParse(IPV4, out var ipAddr))
            {
                WriteExceptionLogToFile("TCP客户端异步连接任务", "参数异常", new Exception($"IP格式非法：{IPV4}"));
                await CloseTCPClientAsync();
                return false;
            }
            ///当TCP客户端已经连接
            if (!TcpClientDisConnected || (tcpClient != null && tcpClient.Connected))
            {
                WriteExceptionLogToFile("TCP客户端异步连接任务", "客户端连接异常", new Exception("TCP客户端已经连接"));
                return false;
            }
            ///TCP客户端正在连接中（防止多次并发连接）
            if (IsConnecting)
                return false;
            #endregion
            IsConnecting = true;   ///开始连接，标记正在连接（防止多次并发连接）
            #region 开始异步连接
            try
            {
                ///先清理旧客户端资源，再创建新实例
                await CloseTCPClientAsync();
                tcpClient = new TcpClient();
                ///重置连接状态
                TcpClientDisConnected = true;
                ///再次检查IP和端口
                if(string.IsNullOrEmpty(IPV4) || port < 1 || port > 65535)
                {
                    WriteExceptionLogToFile("TCP客户端异步连接任务", "参数异常", new Exception("传入IP为空或端口格式有问题"));
                    await CloseTCPClientAsync();
                    return false;
                }
                ///开始纯异步连接，加上超时操作
                var connectTask = tcpClient.ConnectAsync(IPV4, port);
                var timeoutTask = Task.Delay(ConnectTimeOutMS);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                if (completedTask == timeoutTask)
                {
                    WriteExceptionLogToFile("TCP客户端异步连接任务", "连接超时异常",
                        new TimeoutException($"TCP连接超时（超时时间：{ConnectTimeOutMS}毫秒），目标：{IPV4}:{port}"));
                    await CloseTCPClientAsync();
                    return false;
                }
                ///当没有触发超时，等待握手连接完成
                await connectTask.ConfigureAwait(false);
                ///再次检查连接状态
                if (!tcpClient.Connected)
                {
                    WriteExceptionLogToFile("TCP客户端异步连接任务", "连接失败异常",
                        new Exception($"TCP客户端未处于已连接状态，目标：{IPV4}:{port}"));
                    await CloseTCPClientAsync();
                    return false;
                }
                ///握手连接成功，开始为客户端状态标记赋值（已加锁）
                TcpClientDisConnected = false;
                ///赋值成功安全获取网络流并记录并返回true
                stream = GetSafeNetworkStream();
                WriteLogToFile("TCP客户端异步连接任务", $"连接成功，目标：{IPV4}:{port}");
                return true;
            }
            #endregion
            #region TCP客户端异步连接异常部分
            catch(InvalidOperationException inoex)
            {
                TcpClientDisConnected = true;
                WriteExceptionLogToFile("TCP客户端异步连接任务", "网络流异常", inoex);
                await CloseTCPClientAsync();
                return false;
            }
            catch(SocketException scex)
            {
                TcpClientDisConnected = true;
                string errorDesc = scex.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "目标服务器拒绝连接（端口未开放/服务未启动）",
                    SocketError.HostNotFound => "无法解析IP地址（DNS失败）",
                    SocketError.NetworkUnreachable => "网络不可达",
                    SocketError.ConnectionReset => "连接被重置",
                    _ => "通用套接字异常"
                };
                WriteExceptionLogToFile("TCP客户端异步连接任务", $"{errorDesc}（{scex.SocketErrorCode}）", scex);
                await CloseTCPClientAsync();
                return false;
            }
            catch (System.Exception ex)
            {
                TcpClientDisConnected = true;
                WriteExceptionLogToFile("TCP客户端异步连接任务","通用异常",ex);
                await CloseTCPClientAsync();
                return false;
            }
            #endregion
            finally
            {
                ///取消标记正在连接
                IsConnecting = false;
            }
        }

        /// <summary>
        /// 等待式(await)尝试异步重连
        /// </summary>
        /// <param name="ConnectTimeOutMS">重连超时阈值</param>
        /// <param name="token">取消令牌</param>
        /// <returns></returns>
        private async Task TryToReconnectAsync(int ConnectTimeOutMS = 1000,CancellationToken token = default)
        {

        }
        #endregion

        #region 异步发送
        /// <summary>
        /// 等待式(await)异步发送
        /// </summary>
        /// <param name="Msg">需要发送的消息</param>
        /// <param name="token">传入token防止内部异步阻塞</param>
        /// <param name="WaitTimeOut_MS">短暂暂停时长</param>
        /// <param name="WrapBufferMode">封包模式</param>
        /// <returns>发送是否成功</returns>
        private async Task<bool> SendMsgAsync(string Msg,CancellationToken token = default,int WaitTimeOut_MS = 1000,int WrapBufferMode = 0)
        {
            #region 异步发送前检查部分
            ///检查Msg是否为空或空字符
            if (string.IsNullOrEmpty(Msg))
            {
                return false;
            }
            ///检查超时时长
            if (WaitTimeOut_MS < 1000)
                WaitTimeOut_MS = 1000;
            #endregion
            #region 异步发送部分
            try
            {
                token.ThrowIfCancellationRequested();
                ///获取字符串的字节
                byte[] ORsendBuffers = Encoding.UTF8.GetBytes(Msg);
                ///封包处理
                byte[] SendBuffers = WrapSendBytes(ORsendBuffers,WrapBufferMode);
                if (stream != null && SendBuffers.Length > 0)
                {
                    ///超时发送
                    var writeTask = stream.WriteAsync(SendBuffers, 0, SendBuffers.Length,token);
                    var timeoutTask = Task.Delay(WaitTimeOut_MS,token);
                    var completedTask = await Task.WhenAny(writeTask, timeoutTask).ConfigureAwait(false);

                    ///超时后处理
                    if (completedTask == timeoutTask)
                    {
                        WriteExceptionLogToFile("异步发送任务", "发送超时异常",
                            new TimeoutException($"TCP发送消息超时（超时时间：{WaitTimeOut_MS}毫秒），目标：{IPV4}:{port}"));
                        return false;
                    }
                    ///等待发送任务完成
                    await writeTask.ConfigureAwait(false);
                    return true;
                }
                else
                {
                    ///网络流问题
                    if(!IsNetworkStreamValid())
                        WriteExceptionLogToFile("异步发送任务","网络流异常", new Exception("网络流失效"));
                    if (TcpClientDisConnected || tcpClient?.Connected == false)
                        WriteExceptionLogToFile("异步发送任务", "连接异常", new Exception("TCP连接已断开，无法发送"));
                    return false;
                }
            }
            #endregion
            #region TCP客户端异步发送异常部分
            ///捕获空引用异常
            catch(NullReferenceException nrex)
            {
                WriteExceptionLogToFile("异步发送任务","空引用异常",nrex);
                return false;
            }
            ///捕获Token异常
            catch(OperationCanceledException)
            {
                WriteLogToFile("异步发送任务", "发送任务被取消（token触发取消）");
                return false;
            }
            ///捕获套接字异常
            catch (SocketException scex)
            {
                string errorDesc = scex.SocketErrorCode switch
                {
                    SocketError.ConnectionReset => "远程主机强制关闭连接（10054）",
                    SocketError.ConnectionAborted => "连接被中止（10053）",
                    SocketError.NetworkUnreachable => "网络不可达（10051）",
                    SocketError.NotConnected => "套接字未连接（10057）",
                    SocketError.TimedOut => "发送超时（10060）",
                    _ => $"未知套接字错误（错误码：{scex.SocketErrorCode}）"
                };
                WriteExceptionLogToFile("异步发送任务", $"Socket异常：{errorDesc}", scex);
                if (Msg.Contains(_HeartBeatStrSignal))
                {
                    HeartBeatFailCount++;
                    ///心跳包发送失败，尝试重连
                    await TryToReconnectAsync(1000,token);
                    Debug.WriteLine("心跳包发送失败", $"失败次数：{HeartBeatFailCount}，异常：{errorDesc}");
                }
                return false;
            }
            ///捕获数据流（IO）异常
            catch (IOException ioex)
            {

                WriteExceptionLogToFile("异步发送任务", "IO异常", ioex);
                if (Msg.Contains(_HeartBeatStrSignal))
                {
                    HeartBeatFailCount++;
                    ///心跳包发送失败，尝试重连
                    await TryToReconnectAsync(1000,token);
                    Debug.WriteLine($"心跳包发送失败：失败次数：{HeartBeatFailCount}，异常：{ioex.Message}");
                }
                return false;
            }
            ///捕获通用异常
            catch (System.Exception ex)
            {
                WriteExceptionLogToFile("异步发送任务", "通用异常", ex);
                return false;
            }
            #endregion
        }

        /// <summary>
        /// 无阻塞式(安全无阻塞启动)异步自动发送消息(通过消息队列)
        /// </summary>
        /// <param name="token">取消令牌（用于取消任务，使任务自动退出）</param>
        /// <param name="DelayMS">短暂等待时长</param>
        /// <param name="WrapBufferMode">封包模式</param>
        /// <returns></returns>
        protected async Task AutoSendMsg_ByTryDeQueueAsync(CancellationToken token = default,int DelayMS = 400,int WrapBufferMode = 0)
        {
            #region 发送前检查部分
            ///检查等待时长
            if (DelayMS < 500)
                DelayMS = 500;
            #endregion
            Debug.WriteLine("无阻塞异步自动发送后台任务（通过消息队列获取字符串）开始运行");
            #region 循环检查发送消息部分
            while (!token.IsCancellationRequested)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    ///发送队列为空，退出循环
                    if (RecvMsgQueue == null)
                    {
                        WriteLogToFile("无阻塞异步自动发送任务（通过消息队列获取字符串）", "发送消息队列为空");
                        break;
                    }
                    ///发送消息队列无元素，等待时长延长1倍，降低内存占用
                    if (SendMsgQueue != null && SendMsgQueue.Count == 0)
                    {
                        await Task.Delay(DelayMS * 2, token).ConfigureAwait(false);
                        continue;
                    }
                    if (SendMsgQueue != null && SendMsgQueue.Count > 0 && SendMsgQueue.TryDequeue(out string? Msg) && !string.IsNullOrEmpty(Msg))
                    {
                        ///执行安全启动异步方法的函数，并开始安全异步发送
                        var SendResult = await SendMsgAsync(Msg,token,1000,WrapBufferMode);
                        if (SendResult)
                        {
                            ///心跳包发送成功，重置心跳发送失败次数
                            if (Msg.Contains(_HeartBeatStrSignal) && HeartBeatFailCount > 0)
                                HeartBeatFailCount = 0;
                            WriteLogToFile("无阻塞自动异步发送消息", "发送成功");
                        }
                    }
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
                #endregion
                #region 循环检查发送消息异常部分
                ///捕获CTS被取消抛出异常
                catch(OperationCanceledException)
                {
                    break;
                }
                ///捕获空引用问题
                catch (NullReferenceException nrex)
                {
                    ///队列为空则退出循环
                    if (SendMsgQueue == null)
                    {
                        WriteExceptionLogToFile("无阻塞异步自动发送任务（通过消息队列获取字符串）", "空引用异常", new Exception("发送消息队列为空的异常:" + nrex.Message));
                        break;
                    }
                    ///其他为空则短暂等待
                    else
                    {
                        WriteExceptionLogToFile("无阻塞异步自动发送任务（通过消息队列获取字符串）", "空引用异常", new Exception("非(发送消息队列为空)的异常:" + nrex.Message));
                        await Task.Delay(DelayMS, token).ConfigureAwait(false);
                    }
                }
                ///捕获通用异常
                catch (Exception ex)
                {
                	WriteExceptionLogToFile("无阻塞异步自动发送任务（通过消息队列获取字符串）","通用异常",ex);
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
            }
            #endregion
            Debug.WriteLine("无阻塞异步自动发送后台任务（通过消息队列获取字符串）已经结束");
        }
        #endregion

        #region 异步接收
        /// <summary>
        /// 等待式(await)异步接收(获取接收的字符串)
        /// </summary>
        /// <param name="token">传入取消令牌</param>
        /// <returns>接收到的字符串</returns>
        private async Task<string> RecvMsgAsync(CancellationToken token = default)
        {
            #region 异步接收前检查
            ///检查网络流
            if (stream == null)
                return string.Empty;
            #endregion
            #region 异步接收部分
            try
            {
                token.ThrowIfCancellationRequested();
                ///检查Socket是否有可读数据
                var socket = tcpClient.Client;
                if (!socket.Poll(100, SelectMode.SelectRead)) 
                {
                    ///无数据时直接返回
                    return string.Empty; 
                }
                if (!TcpClientDisConnected && tcpClient?.Connected == true)
                {
                    ///预留8Kb字节接收
                    byte[] RecvBuffer = new byte[RecvbufferSize];
                    int actualReadLength = 0;

                    ///加锁防止多个异步读取任务同时读取同一块字节块导致数据重复
                    await RecvAsyncLock.WaitAsync(token);
                    try
                    {
                        actualReadLength = await stream.ReadAsync(RecvBuffer, 0, RecvBuffer.Length, token).ConfigureAwait(false);
                        if (actualReadLength == 0)
                        {
                            ///读取长度为0说明服务端关闭连接
                            WriteLogToFile("异步接收消息任务", "接收字节长度为0，服务端已断开连接");
                            return string.Empty;
                        }
                        ///获取到字符串
                        string recvStr = Encoding.UTF8.GetString(RecvBuffer, 0, actualReadLength);
                        return recvStr;
                    }
                    catch (OperationCanceledException)
                    {
                        return string.Empty;
                    }
                    finally
                    {
                        ///释放异步锁
                        RecvAsyncLock.Release();
                    }
                }
                else
                {
                    ///连接已断开
                    WriteExceptionLogToFile("异步接收消息任务", "连接异常",
                        new Exception("TCP连接已断开，无法接收数据"));
                    return string.Empty;
                }
            }
            #endregion
            #region TCP客户端异步接收异常部分
            ///捕获空异常
            catch (NullReferenceException nrex)
            {
                if (!IsNetworkStreamValid())
                    WriteExceptionLogToFile("异步接收消息任务", "空引用异常", new Exception("网络流为空"));
                else
                    WriteExceptionLogToFile("异步接收消息任务", "空引用异常", new Exception("非网络流为空的异常:" + nrex.Message));
                return string.Empty;
            }
            ///捕获Token异常
            catch (OperationCanceledException)
            {
                WriteLogToFile("异步接收消息任务", "接收任务被取消（token触发取消）");
                return string.Empty;
            }
            ///捕获套接字异常
            catch (SocketException scex)
            {
                string errorDesc = scex.SocketErrorCode switch
                {
                    SocketError.ConnectionReset => "远程主机强制关闭连接（10054）",
                    SocketError.ConnectionAborted => "连接被中止（10053）",
                    SocketError.NetworkUnreachable => "网络不可达（10051）",
                    SocketError.NotConnected => "套接字未连接（10057）",
                    SocketError.TimedOut => "发送超时（10060）",
                    _ => $"未知套接字错误（错误码：{scex.SocketErrorCode}）"
                };
                WriteExceptionLogToFile("异步接收消息任务", $"Socket异常：{errorDesc}", scex);
                return string.Empty;
            }
            ///捕获网络流和其他数据流(IO)异常
            catch (IOException ioex)
            {
                WriteExceptionLogToFile("异步接收消息任务", "IO异常", ioex);
                return string.Empty;
            }
            ///捕获通用异常
            catch (System.Exception ex)
            {
                WriteExceptionLogToFile("异步接收消息任务", "通用异常", ex);
                return string.Empty;
            }
            #endregion
        }

        /// <summary>
        /// 无阻塞式（安全无阻塞启动）异步自动接收消息（通过消息队列）
        /// </summary>
        /// <param name="token">传入的取消令牌</param>
        /// <param name="DelayMS">短暂等待时长</param>
        /// <returns></returns>
        protected async Task AutoRecvMsg_ByEnQueueAsync(CancellationToken token = default,int DelayMS = 300)
        {
            #region 异步自动接收前检查部分
            ///检查等待时长
            if (DelayMS < 100)
                DelayMS = 100;
            #endregion
            Debug.WriteLine("无阻塞异步接收消息后台任务（通过消息入列）开始运行");
            #region 异步自动接收部分
            while (!token.IsCancellationRequested)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (RecvMsgQueue == null)
                    {
                        WriteLogToFile("无阻塞异步接收消息后台任务（通过消息入列）", "发送消息队列为空");
                        break;
                    }
                    string RecvStr = await RecvMsgAsync(token);
                    if (RecvMsgQueue != null && !string.IsNullOrEmpty(RecvStr))
                        RecvMsgQueue.Enqueue(RecvStr);
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
                #endregion
                #region 异步自动接收异常部分
                ///捕获CTS被取消抛出异常
                catch (OperationCanceledException)
                {
                    break;
                }
                ///捕获空引用问题
                catch (NullReferenceException nrex)
                {
                    if(RecvMsgQueue == null)
                    {
                        WriteExceptionLogToFile("无阻塞异步接收消息后台任务（通过消息入列）","空引用异常",new Exception("接收消息队列为空的异常:" + nrex.Message));
                        break;
                    }
                    else
                    {
                        WriteExceptionLogToFile("无阻塞异步接收消息后台任务（通过消息入列）", "空引用异常", new Exception("非(接收消息队列为空)的异常:" + nrex.Message));
                        await Task.Delay(DelayMS, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    WriteExceptionLogToFile("无阻塞异步接收消息后台任务（通过消息入列）", "通用异常", ex);
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
            }
            #endregion
            Debug.WriteLine("无阻塞异步接收消息后台任务（通过消息入列）已经结束");
        }
        #endregion

        #region 异步更新和分析
        /// <summary>
        /// 无阻塞式（安全无阻塞启动）异步自动更新UI（使用事件）委托和分析接收的数据（通过消息队列）
        /// </summary>
        /// <param name="token">传入取消令牌</param>
        /// <param name="DelayMS">短暂等待时长</param>
        /// <param name="NeedAnalysisByAsyncIO">是否需要使用异步IO方法解析接收到的字符串</param>
        /// <returns></returns>
        protected async Task AutoInvokeUIAndAnalysisData_ByDeQueue(CancellationToken token = default,int DelayMS = 100,bool NeedAnalysisByAsyncIO = true)
        {
            #region 异步更新和分析前检查
            ///检查等待时长
            if (DelayMS < 100)
                DelayMS = 100;
            #endregion
            Debug.WriteLine("异步更新UI和分析数据后台任务开始运行");
            #region 异步更新UI和分析数据部分
            while (!token.IsCancellationRequested)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    ///接收队列为空，退出循环
                    if (RecvMsgQueue == null)
                    {
                        WriteLogToFile("无阻塞异步更新UI和分析消息后台任务（通过消息出列）", "接收消息队列为空");
                        break;
                    }
                    ///接收队列无元素，延长等待时长
                    if(RecvMsgQueue != null && RecvMsgQueue.Count == 0)
                    {
                        await Task.Delay(DelayMS * 2, token);
                        continue;
                    }
                    if (RecvMsgQueue != null && RecvMsgQueue.Count > 0 && RecvMsgQueue.TryDequeue(out string? RecvStr) && !string.IsNullOrEmpty(RecvStr))
                    {
                        ///异步更新事件(当委托的事件存在)
                        InvokeUI?.Invoke(RecvStr);
                        if (NeedAnalysisByAsyncIO)
                        {
                            ///分析数据(使用异步IO解析数据时调用)
                            var analysisResult = await AnalysisRecvData(token, 500, RecvStr);
                            if (analysisResult)
                            {
                                WriteLogToFile("无阻塞异步更新UI和分析消息后台任务（通过消息出列）", "成功分析接收到的数据");
                            }
                        }
                        else
                            NeedAnalysisByCPUAsync_RecvMsgQeueue?.Enqueue(RecvStr);
                    }
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
                #endregion
                #region 异步更新UI和分析数据异常部分
                ///捕获CTS被取消抛出异常
                catch (OperationCanceledException)
                {
                    break;
                }
                ///捕获空引用问题
                catch (NullReferenceException nrex)
                {
                    ///队列为空则退出循环
                    if (RecvMsgQueue == null)
                    {
                        WriteExceptionLogToFile("无阻塞异步更新UI和分析消息后台任务（通过消息出列）", "空引用异常", new Exception("接收消息队列为空的异常:" + nrex.Message));
                        break;
                    }
                    ///其他为空则短暂等待
                    else
                    {
                        WriteExceptionLogToFile("无阻塞异步更新UI和分析消息后台任务（通过消息出列）", "空引用异常", new Exception("非(接收消息队列为空)的异常:" + nrex.Message));
                        await Task.Delay(DelayMS, token).ConfigureAwait(false);
                    }
                }
                ///捕获通用异常
                catch (Exception ex)
                {
                    WriteExceptionLogToFile("无阻塞异步更新UI和分析消息后台任务（通过消息出列）", "通用异常", ex);
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
            }
            #endregion
            Debug.WriteLine("异步更新UI和分析数据后台任务已经结束");
        }
        #endregion

        #region 心跳包机制（长时间维持连接稳定）
        /// <summary>
        /// 释放旧定时器并初始化新定时器
        /// </summary>
        protected void IniHeartBeat()
        {
            StopHeartBeat();
            heartbeattimer = new Timer(EnqueueHeartBeatStrToQueue, null,0, _heartBeatIntervalMS);
        }

        /// <summary>
        /// 定时器委托函数，定时心跳包入列
        /// </summary>
        /// <param name="state"></param>
        private void EnqueueHeartBeatStrToQueue(object? state)
        {
            lock(_timerCallbackLock)
            {
                EnqueueStrToQueue(_HeartBeatStrSignal);
            }
        }

        /// <summary>
        /// 停止心跳包发送
        /// </summary>
        private void StopHeartBeat()
        {
            lock (_timerCallbackLock)
            {
                ///结束定时器进程
                heartbeattimer?.Change(Timeout.Infinite, Timeout.Infinite);
                heartbeattimer?.Dispose();
                ///重置心跳发送失败次数
                HeartBeatFailCount = 0;
                ///定时器置空
                heartbeattimer = null;
                if (!TcpClientDisConnected)
                    Debug.WriteLine("旧定时器已经停止工作了");
                else
                    Debug.WriteLine("该定时器已经停止工作了");
            }
        }

        /// <summary>
        /// 异步监测心跳失败次数是否超出阈值的后台任务（假如超出则直接调用CloseTCPClientAsync，实现当服务器关闭时，客户端能够自主退出，而非需要人工退出）
        /// </summary>
        /// <param name="token">传入取消令牌</param>
        /// <param name="DelayMS">短暂暂停时长</param>
        /// <returns></returns>
        protected async Task AutoCheckHeartBeatFailTime_AndRespose(CancellationToken token = default,int DelayMS = 300)
        {
            #region 异步监测心跳失败次数是否超出阈值前的检查
            if (DelayMS < 0)
            {
                DelayMS = 1000;
            }
            #endregion
            Debug.WriteLine("异步检查心跳失败次数是否超出阈值的后台任务开始运行");
            #region 异步监测心跳失败次数是否超出阈值部分
            while (!token.IsCancellationRequested)
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    ///超出心跳发送失败阈值，自主结束TCP进程
                    if (HeartBeatFailCount > _heartBeatTimeoutThreshold)
                    {
                        await CloseTCPClientAsync();
                    }
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
                #endregion
                #region 异步监测心跳失败次数是否超出阈值的异常部分
                catch (OperationCanceledException)
                {
                    ///执行CloseTCPClientAsync后取消令牌会释放，会捕捉该类型异常从而让该异步方法优雅地退出
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(DelayMS, token).ConfigureAwait(false);
                }
            }
            #endregion
            Debug.WriteLine("异步检查心跳失败次数是否超出阈值的后台任务已经结束");
        }
        #endregion

        #region 异步关闭TCP
        /// <summary>
        /// 异步取消所有任务，清理所有资源
        /// 清理顺序: 清理CTS -> 等待并清理后台任务 -> 清理容器 -> 清理TCP资源（关闭客户端）
        /// </summary>
        /// <returns></returns>
        protected async Task CloseTCPClientAsync()
        {
            TcpClientDisConnected = true;
            ///停止定时器工作
            StopHeartBeat();
            ///取消令牌被取消，用于退出所有无阻塞异步任务
            CleanupAllTcpClientCts();
            ///记录后台任务被清理的数量
            await CleanupAllBackgroundTasks().ConfigureAwait(false);
            ///清理TCP所有容器资源
            CleanupAllTcpClientDataContainers();
            ///清理TCP客户端资源，关闭连接
            CleanupTcpClient();
        }
        #endregion
        #endregion
        
        #region 基类接口，供子类重写扩展
        /// <summary>
        /// 分析数据接口(虚函数)
        /// =>使用Task.Run来启动CPU密集计算的异步任务,需要传入token并在Lambda里面主动调用token.ThrowIfCancellationRequested()并捕获OperationCanceledException来退出
        /// </summary>
        /// <returns>分析是否成功结束</returns>
        protected virtual async Task<bool> AnalysisRecvData(CancellationToken token = default,int WaitTimeOut_MS = 500,string _RecvStr = "")
        {
            await Task.CompletedTask;
            return true;
        }

        /// <summary>
        /// 字节粘包默认处理（只含"无处理"和"加大端序"这两种默认方法），默认无封包模式
        /// </summary>
        /// <param name="originalBytes">字符串转字节数组</param>
        /// <param name="mode">封包模式（0=无封包，1=加大端序长度前缀）</param>
        /// <returns>长度前缀 + 原始字节的拼接数组</returns>
        protected virtual byte[] WrapSendBytes(byte[] ORsendBuffer,int mode = 0)
        {
            switch(mode)
            {
                case 1:
                    ///通过大端序前缀4字节表示内容长度
                    int dataLength = ORsendBuffer.Length;
                    byte[] lengthPrefix = BitConverter.GetBytes(IPAddress.HostToNetworkOrder(dataLength));

                    ///拼接大端序和内容字节数组
                    byte[] wrappedBytes = new byte[lengthPrefix.Length + ORsendBuffer.Length];
                    Buffer.BlockCopy(lengthPrefix, 0, wrappedBytes, 0, lengthPrefix.Length);
                    Buffer.BlockCopy(ORsendBuffer, 0, wrappedBytes, lengthPrefix.Length, ORsendBuffer.Length);
                    return wrappedBytes;
                case 0:
                    ///无封包模式直接返回原字节Arr
                    return ORsendBuffer;
                default:
                    ///默认无封包处理
                    return ORsendBuffer;
            }
        }

        /// <summary>
        /// 拆包
        /// </summary>
        /// <param name="ORRecvBuffer">接收到的字节Arr</param>
        /// <param name="mode">拆包模式</param>
        /// <returns></returns>
        protected virtual byte[] UnpackRecvBuffer(byte[] ORRecvBuffer,int mode = 0)
        {
            int ORBufferLen = ORRecvBuffer.Length;
            if (ORRecvBuffer == null || ORBufferLen == 0)
                return Array.Empty<byte>();
            switch (mode)
            {
                case 1:
                    return ORRecvBuffer;
                case 0:
                    ///无封包模式直接返回原字节Arr
                    return ORRecvBuffer;
                default:
                    ///其他mode值默认无封包处理
                    return ORRecvBuffer;
            }
        }
        #endregion


        #region 日志函数
        /// <summary>
        /// 写入普通日志到Log.txt
        /// </summary>
        /// <param name="message">日志信息</param>
        /// <param name="TaskName">任务或函数名称</param>
        public void WriteLogToFile(string TaskName,string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            lock (_fileLock)
            {
                try
                {
                    string logContent =
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}]{Environment.NewLine}" +
                        $"任务或函数名称: {TaskName}{Environment.NewLine}" +
                        $"消息: {message}{Environment.NewLine}" +
                        "----------------------------------------" + Environment.NewLine;
                    File.AppendAllText(_logFilePath, logContent, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"写入日志文件失败: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 写入异常日志到ExLog.txt
        /// </summary>
        /// <param name="ex">异常对象</param>
        /// <param name="ExceptionType">异常类型</param>
        /// <param name="TaskName">任务或函数名称</param>
        public void WriteExceptionLogToFile(string TaskName,string ExceptionType,Exception ex)
        {
            if (ex == null) return;
            lock (_fileLock)
            {
                try
                {
                    ///异常日志包含详细信息(类型、消息、堆栈)
                    string exLogContent =
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 抛出异常类型: {ex.GetType().FullName}{Environment.NewLine}" +
                        $"任务或函数名称: {TaskName}   ;   异常类型: {ExceptionType}{Environment.NewLine}" +
                        $"异常消息: {ex.Message}{Environment.NewLine}" +
                        $"堆栈跟踪: {ex.StackTrace}{Environment.NewLine}" +
                        "----------------------------------------" + Environment.NewLine;
                    File.AppendAllText(_exceptionLogFilePath, exLogContent, Encoding.UTF8);
                }
                catch (Exception writeEx)
                {
                    Debug.WriteLine($"写入异常日志文件失败: {writeEx.Message}");
                }
            }
        }

        /// <summary>
        /// 检查日志文件大小，超过阈值则清空内容
        /// </summary>
        /// <param name="filePath">文件路径</param>
        private void CheckAndClearLogFile(string filePath)
        {
            ///复用文件锁保证线程安全
            lock (_fileLock) 
            {
                try
                {
                    ///判断文件是否存在
                    if (File.Exists(filePath))
                    {
                        FileInfo fileInfo = new FileInfo(filePath);
                        ///阈值变量判断(4Mb)
                        if (fileInfo.Length > _logFileSizeThreshold)
                        {
                            ///清空文件内容
                            File.WriteAllText(filePath, string.Empty, Encoding.UTF8);
                        }
                    }
                }
                catch (UnauthorizedAccessException uaex)
                {
                    Debug.WriteLine($"检查/清理日志文件 {filePath} 失败:{uaex.Message}");
                    WriteExceptionLogToFile("检查/清理日志文件失败", "文件读取写入权限不足异常", uaex);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"检查/清理日志文件 {filePath} 失败：{ex.Message}");
                    WriteExceptionLogToFile("检查/清理日志文件失败", "文件清理异常", ex);
                }
            }
        }
        #endregion
        #region 辅助封装函数
        #region 获取某个值的函数
        /// <summary>
        /// 安全获取数据流
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private NetworkStream GetSafeNetworkStream()
        {
            if (tcpClient == null)
                throw new InvalidOperationException("TCP客户端未初始化");
            if (TcpClientDisConnected || !tcpClient.Connected)
                throw new InvalidOperationException("TCP客户端未连接");
            return tcpClient.GetStream();
        }
        #endregion
        #region 创建(New)函数
        /// <summary>
        /// 创建客户端所有存储数据容器
        /// </summary>
        public void NewAllTCPClientDataContainers()
        {
            if(RecvMsgQueue == null)
            {
                RecvMsgQueue = new ConcurrentQueue<string>();
            }
            if(NeedAnalysisByCPUAsync_RecvMsgQeueue == null)
            {
                NeedAnalysisByCPUAsync_RecvMsgQeueue = new ConcurrentQueue<string>();
            }
            if(SendMsgQueue == null)
            {
                SendMsgQueue = new ConcurrentQueue<string>();
            }
        }
        /// <summary>
        /// 创建所有Cts，用来启动任务
        /// </summary>
        public void NewAllTCPClientCts()
        {
            if (AutoRecvMsgcts == null)
            {
                AutoRecvMsgcts = new CancellationTokenSource();
            }
            if(AutoSendMsgcts == null)
            {
                AutoSendMsgcts = new CancellationTokenSource();
            }
            if (InvokeAndAnalysisRecvDatacts == null)
            {
                InvokeAndAnalysisRecvDatacts = new CancellationTokenSource();
            }
            if (HeartbeatCheckcts == null)
            {
                HeartbeatCheckcts = new CancellationTokenSource();
            }
        }
        #endregion
        #region 清理函数
        /// <summary>
        /// 清理客户端套接字和数据流资源
        /// </summary>
        private void CleanupTcpClient()
        {
            try
            {
                ///关闭网络流
                if (stream != null)
                {
                    try
                    {
                        stream.Close();
                        stream.Dispose();
                    }
                    catch (Exception ex)
                    {
                        WriteExceptionLogToFile("资源清理", "关闭网络流异常", ex);
                    }
                    finally
                    {
                        stream = null;
                    }
                }

                ///关闭TcpClient
                if (tcpClient != null)
                {
                    try
                    {
                        tcpClient.Close();
                        tcpClient.Dispose();
                    }
                    catch (Exception ex)
                    {
                        WriteExceptionLogToFile("资源清理", "关闭TcpClient异常", ex);
                    }
                    finally
                    {
                        tcpClient = null;
                    }
                }

                ///标记断开连接
                TcpClientDisConnected = true;
                WriteLogToFile("清理客户端套接字和数据流任务", "成功释放所有TCP资源");
            }
            catch (Exception ex)
            {
                WriteExceptionLogToFile("清理客户端套接字和数据流任务", "释放TCP资源失败", ex);
            }
        }
        /// <summary>
        /// 清理客户端所有存储数据容器
        /// </summary>
        private void CleanupAllTcpClientDataContainers()
        {
            try
            {
                if (RecvMsgQueue != null)
                {
                    RecvMsgQueue?.Clear();
                    RecvMsgQueue = null;
                }
                if(NeedAnalysisByCPUAsync_RecvMsgQeueue != null)
                {
                    NeedAnalysisByCPUAsync_RecvMsgQeueue?.Clear();
                    NeedAnalysisByCPUAsync_RecvMsgQeueue = null;
                }
                if (SendMsgQueue != null)
                {
                    SendMsgQueue?.Clear();
                    SendMsgQueue = null;
                }
                WriteLogToFile("清理TCP客户端容器任务","所有容器都清理完毕");
            }
            catch (Exception ex)
            {
                WriteExceptionLogToFile("清理TCP客户端容器任务", "清理容器异常", ex);
            }
        }
        /// <summary>
        /// 清理所有Cts,用来取消异步任务
        /// </summary>
        private void CleanupAllTcpClientCts()
        {
            try
            {
                ///强制取消所有令牌
                AutoRecvMsgcts?.Cancel();
                AutoSendMsgcts?.Cancel();
                InvokeAndAnalysisRecvDatacts?.Cancel();
                HeartbeatCheckcts?.Cancel();

                ///释放令牌资源
                AutoRecvMsgcts?.Dispose();
                AutoSendMsgcts?.Dispose();
                InvokeAndAnalysisRecvDatacts?.Dispose();
                HeartbeatCheckcts?.Dispose();

                ///置空
                AutoRecvMsgcts = null;
                AutoSendMsgcts = null;
                InvokeAndAnalysisRecvDatacts = null;
                HeartbeatCheckcts = null;

                WriteLogToFile("清理CTS任务", "所有取消令牌已强制取消并释放");
            }
            catch(Exception ex)
            {
                WriteExceptionLogToFile("清理CTS任务", "清理取消令牌异常", ex);
            }
        }

        /// <summary>
        /// 异步清理后台任务
        /// </summary>
        /// <returns></returns>
        private async Task CleanupBackgroundTasksAsync()
        {
            List<Task> tasksToWait;
            lock (_BackgroundTasklock)
            {
                if (BackgroundTaskList.Count == 0)
                {
                    WriteLogToFile("清理后台任务", "无待清理的后台任务，直接退出");
                    return;
                }
                tasksToWait = new List<Task>(BackgroundTaskList);
                WriteLogToFile("清理后台任务", $"开始清理{tasksToWait.Count}个后台任务");
            }
            try
            {
                var timeout = TimeSpan.FromSeconds(5);
                var waitAllTask = Task.WhenAll(tasksToWait);
                var timeoutTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(waitAllTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    ///记录未完成任务详情
                    var uncompletedTasks = tasksToWait.Where(t => !t.IsCompleted).ToList();
                    var uncompletedMsg = $"后台任务清理超时（超时{timeout.TotalSeconds}秒）：{uncompletedTasks.Count}个后台任务未退出，已放弃等待。" +
                                         $"未完成后台任务状态：{string.Join(" | ", uncompletedTasks.Select(t => $"任务ID={t.Id},状态={t.Status}"))}";
                    WriteLogToFile("清理后台任务", uncompletedMsg);
                    Debug.WriteLine(uncompletedMsg);

                    ///强制释放网络资源（让残留任务的IO操作失败，从而自然退出）
                    lock (_fileLock)
                    {
                        stream?.Dispose();
                        tcpClient?.Close();
                        tcpClient?.Dispose();
                    }
                    ///清空队列，让任务无数据可处理
                    RecvMsgQueue?.Clear();
                    NeedAnalysisByCPUAsync_RecvMsgQeueue?.Clear();
                    SendMsgQueue?.Clear();
                }
                else
                {
                    ///记录成功日志
                    var successMsg = $"后台任务清理完成：{tasksToWait.Count}个后台任务已正常退出（完成状态：{string.Join(" | ", tasksToWait.Select(t => $"ID={t.Id},状态={t.Status}"))}）";
                    WriteLogToFile("清理后台任务", successMsg);
                    Debug.WriteLine(successMsg);
                    ///等待WhenAll完成
                    await waitAllTask.ConfigureAwait(false);
                }
            }
            catch (AggregateException ex)
            {
                var flatEx = ex.Flatten();
                var exMsg = $"清理后台任务时捕获聚合异常，共{flatEx.InnerExceptions.Count}个内部异常：" +
                            $"{Environment.NewLine}{string.Join(Environment.NewLine, flatEx.InnerExceptions.Select(e => $"异常类型：{e.GetType().Name}，消息：{e.Message}"))}";
                WriteExceptionLogToFile("清理后台任务", "聚合异常", ex);
                Debug.WriteLine(exMsg);
            }
            catch (Exception ex)
            {
                ///捕获通用异常
                WriteExceptionLogToFile("清理后台任务", "通用异常", ex);
                Debug.WriteLine($"任务清理异常：{ex.Message}，堆栈：{ex.StackTrace}");
            }
            finally
            {
                ///清空任务列表（无论成功/失败/超时）
                lock (_BackgroundTasklock)
                {
                    var clearCount = BackgroundTaskList.Count;
                    BackgroundTaskList.Clear();
                    WriteLogToFile("清理后台任务", $"最终清空后台任务列表，共清理{clearCount}个任务记录");
                }
            }
        }

        /// <summary>
        /// 异步清理所有后台任务
        /// </summary>
        private async Task CleanupAllBackgroundTasks()
        {
            await CleanupBackgroundTasksAsync().ConfigureAwait(false);
        }
        #endregion
        #region 安全启动和清理异步
        /// <summary>
        /// 针对返回Task的安全启动异步方法的类方法（解决无阻塞异步问题）
        /// </summary>
        /// <param name="taskFunc">需要启动的函数</param>
        /// <param name="token">传入的取消令牌</param>
        /// <param name="taskName">任务名称</param>
        /// <returns></returns>
        protected void RunSafeAsyncFunc(Func<CancellationToken, Task> taskFunc, CancellationToken token = default, string taskName = "未命名异步任务")
        {
            ///包装任务
            async Task WrappedTask()
            {
                try
                {
                    await taskFunc(token).ConfigureAwait(false);
                }
                catch (AggregateException ex)
                {
                    foreach (var innerEx in ex.Flatten().InnerExceptions)
                    {
                        if (innerEx is OperationCanceledException || innerEx is TaskCanceledException)
                        {
                            WriteLogToFile("安全启动异步方法的方法", $"{taskName}：任务因取消令牌触发正常退出");
                        }
                        else if (innerEx is ObjectDisposedException)
                        {
                            WriteLogToFile("安全启动异步方法的方法", $"{taskName}：资源已释放，任务正常退出");
                        }
                        else
                        {
                            WriteExceptionLogToFile("安全启动异步方法的方法", $"[{taskName}]内部聚合异常", innerEx);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    WriteLogToFile("安全启动异步方法的方法", $"{taskName}：任务因取消令牌触发正常退出");
                }
                catch (ObjectDisposedException)
                {
                    WriteLogToFile("安全启动异步方法的方法", $"{taskName}：资源已释放，任务正常退出");
                }
                catch (SocketException ex)
                {
                    string errorDesc = ex.SocketErrorCode switch
                    {
                        SocketError.ConnectionReset => "远程主机强制关闭连接（10054）",
                        SocketError.ConnectionAborted => "连接被中止（10053）",
                        SocketError.NetworkUnreachable => "网络不可达（10051）",
                        SocketError.NotConnected => "套接字未连接（10057）",
                        SocketError.TimedOut => "操作超时（10060）",
                        _ => $"未知套接字错误（{ex.SocketErrorCode}）"
                    };
                    WriteExceptionLogToFile("安全启动异步方法的方法", $"[{taskName}]Socket异常：{errorDesc}", ex);
                }
                catch (IOException ex)
                {
                    WriteExceptionLogToFile("安全启动异步方法的方法", $"[{taskName}]IO流异常", ex);
                }
                catch (NullReferenceException ex)
                {
                    WriteExceptionLogToFile("安全启动异步方法的方法", $"[{taskName}]空引用异常", ex);
                }
                catch (Exception ex)
                {
                    WriteExceptionLogToFile("安全启动异步方法的方法", $"[{taskName}]未预期的通用异常", ex);
                }
            }

            ///无阻塞启动任务
            var task = WrappedTask();
            ///线程安全添加到任务跟踪列表
            lock (_BackgroundTasklock)
            {
                BackgroundTaskList.Add(task);
            }
            ///任务完成后自动移除
            _ = task.ContinueWith(t =>
            {
                lock (_BackgroundTasklock)
                {
                    if (BackgroundTaskList.Contains(t))
                        BackgroundTaskList.Remove(t);
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }
        #endregion
        #region 其他功能辅助函数
        /// <summary>
        /// 消息入列
        /// </summary>
        /// <param name="Msg">需要入列的字符串</param>
        /// <param name="C_Queue">被入列的安全队列</param>
        public void EnqueueStrToQueue(string Msg)
        {
            try
            {
                if (SendMsgQueue != null && !string.IsNullOrEmpty(Msg))
                    SendMsgQueue.Enqueue(Msg);
            }
            catch (NullReferenceException nrex)
            {
                WriteExceptionLogToFile("消息入列任务", "空引用异常", new Exception("发送消息队列为空" + nrex.Message));
            }
        }

        /// <summary>
        /// 校验网络流是否有效
        /// (null校验 -> 连接状态校验 -> 流隐性有效性校验)
        /// </summary>
        /// <returns>true=有效;false=失效</returns>
        private bool IsNetworkStreamValid()
        {
            ///基础null校验
            if (stream == null) return false;

            ///检查网络流是否可读写
            if (!stream.CanRead || !stream.CanWrite) return false;

            ///TCP连接状态基础校验
            try
            {
                if (tcpClient == null || !tcpClient.Connected || TcpClientDisConnected) return false;

                var socket = tcpClient.Client;
                ///Poll检测（100ms超时）
                if (socket.Poll(100, SelectMode.SelectRead) && socket.Available == 0)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 检查日志文件大小是否超出阈值并进行清理
        /// </summary>
        protected void CheckAndClearAllLogFile()
        {
            ///检查日志文件大小是否超过阈值(4Mb)
            CheckAndClearLogFile(_logFilePath);
            CheckAndClearLogFile(_exceptionLogFilePath);
        }
        #endregion
        #endregion
    }
}
