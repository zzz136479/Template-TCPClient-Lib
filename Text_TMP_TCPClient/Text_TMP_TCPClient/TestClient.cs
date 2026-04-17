using System;
using System.Diagnostics;
using TMP_TCPClient;

namespace Text_TMP_TCPClient
{
    public class TestClient : TCPClient
    {
        public TestClient(int _port, string _IPV4) : base(_port, _IPV4)
        {

        }

        public async Task Start()
        {
            var ConnectResult = await ConnectAsync(3000);
            if(ConnectResult)
            {
                ///连接成功，开始新建取消令牌、容器和检查日志大小是否超出阈值
                CheckAndClearAllLogFile();
                NewAllTCPClientCts();
                NewAllTCPClientDataContainers();
                await Task.Delay(100);
                if (AutoSendMsgcts != null)
                {
                    RunSafeAsyncFunc(token => AutoSendMsg_ByTryDeQueueAsync(token, 50,1), AutoSendMsgcts.Token, "异步自动发送后台任务（通过消息队列）");
                    RunSafeAsyncFunc(token => AutoSendMsg_ByTryDeQueueAsync(token, 50,1), AutoSendMsgcts.Token, "异步自动发送后台任务（通过消息队列）");
                    RunSafeAsyncFunc(token => AutoSendMsg_ByTryDeQueueAsync(token, 50,1), AutoSendMsgcts.Token, "异步自动发送后台任务（通过消息队列）");
                }
                await Task.Delay(100);
                if (AutoRecvMsgcts != null)
                {
                    RunSafeAsyncFunc(token => AutoRecvMsg_ByEnQueueAsync(token, 100), AutoRecvMsgcts.Token, "异步自动接收后台任务1（通过消息队列）");
                    RunSafeAsyncFunc(token => AutoRecvMsg_ByEnQueueAsync(token, 100), AutoRecvMsgcts.Token, "异步自动接收后台任务2（通过消息队列）");
                }
                await Task.Delay(100);
                if (InvokeAndAnalysisRecvDatacts != null)
                {
                    RunSafeAsyncFunc(token => AutoInvokeUIAndAnalysisData_ByDeQueue(token, 100), InvokeAndAnalysisRecvDatacts.Token, "异步自动更新UI和分析接收的字符串的后台任务1（通过消息队列）");
                    RunSafeAsyncFunc(token => AutoInvokeUIAndAnalysisData_ByDeQueue(token, 100), InvokeAndAnalysisRecvDatacts.Token, "异步自动更新UI和分析接收的字符串的后台任务1（通过消息队列）");
                }
                await Task.Delay(100);
                if (HeartbeatCheckcts != null)
                {
                    IniHeartBeat();
                    RunSafeAsyncFunc(token => AutoCheckHeartBeatFailTime_AndRespose(token,1000),HeartbeatCheckcts.Token, "异步自动监测心跳失败次数是否超出阈值的后台任务1（通过HeartBeatFailTime）");
                }
            }
        }

        public async Task Close()
        {
            await CloseTCPClientAsync();
        }

        protected override Task<bool> AnalysisRecvData(CancellationToken token = default, int WaitTimeOut_MS = 500, string _RecvStr = "")
        {
            Debug.WriteLine("使用了子类重写的分析数据方法");
            return base.AnalysisRecvData(token, WaitTimeOut_MS, _RecvStr);
        }
    }
}
