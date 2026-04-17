# TMP\_TCPClient



## 中文：

这个是我做的一个TCP模板库，里面封装了异步连接、异步接收和异步发送，异步发送和异步接收都是通过Concurrent<string>组成消息队列，分别作为发送消息和接收消息的缓冲区。文件里面提供了**AutoSendMsg\_ByTryDeQueueAsync**、**AutoRecvMsg\_ByEnQueueAsync**、**AutoInvokeUIAndAnalysisData\_ByDeQueue**和**AutoCheckHeartBeatFailTime\_AndRespose**四大方法，作为后台任务（发送、接收、UI更新和数据分析、检查心跳失败次数是否超出阈值），其中**AutoSendMsg\_ByTryDeQueueAsync**、**AutoRecvMsg\_ByEnQueueAsync**和**AutoInvokeUIAndAnalysisData\_ByDeQueue方法的中断机制是传入的CancellationTokenSource和判断消息队列是否为null。**



附件Test\_TMP\_TCPClient：这个是用来测试的winform客户端，使用者可以参考源码中如何开启后台任务的格式。



目前该库有许多问题，比如我在测试给服务器发送字符串时，有**粘包问题，**如发送String:“333"并连续点击4次后，有时候能在服务器看到的是333333 333 333，而非333 333 333 333。还有该库并没有实现重连机制(Reconnect)，只有**心跳机制+自动断开连接**。后面我写了WrapSendBytes方法，在发送前把字节做了封包处理（使用**大端序**，4字节），但同样的，服务器也需要对应有拆包过程。我测试客户端接收数据时，可能是因为在获取stream读取到的字节时加了**异步锁**，没有出现字节读取混乱、缺失或粘包情况，所以我的RecvMsgAsync方法里面并没有拆包过程，如使用者有遇到，可以给我发送邮件，也可以在github留言



邮箱：zzz136479@qq.com







## English:

This is a TCP template library I developed, which encapsulates asynchronous connection, asynchronous receiving, and asynchronous sending. Both asynchronous sending and asynchronous receiving rely on Concurrent<string> to form message queues, serving as buffers for sending and receiving messages respectively. The file provides four core methods: **AutoSendMsg\_ByTryDeQueueAsync**, **AutoRecvMsg\_ByEnQueueAsync**, **AutoInvokeUIAndAnalysisData\_ByDeQueue**, and **AutoCheckHeartBeatFailTime\_AndRespose**. These act as background tasks (handling sending, receiving, UI update \& data analysis, and checking whether the heartbeat failure count exceeds the threshold). Among them, the interruption mechanism of **AutoSendMsg\_ByTryDeQueueAsync**, **AutoRecvMsg\_ByEnQueueAsync**, and **AutoInvokeUIAndAnalysisData\_ByDeQueue** funtions depends on the passed CancellationTokenSource and the judgment of whether the message queue is null.



Attachment **Test\_TMP\_TCPClient**: This is a WinForm client for testing. Users can refer to the format in the source code to learn how to start background tasks.



Currently, this library has several known issues. For example, during my test of sending strings to the server, a packet sticking issue occurred: when sending the string "333" by clicking the send button 4 times in a row, the server sometimes receives "333333 333 333" instead of the expected "333 333 333 333". Additionally, the library does not implement a reconnection mechanism (Reconnect), only a heartbeat mechanism plus automatic disconnection. Later, I wrote the WrapSendBytes method, which performs packet encapsulation on bytes before sending (using big-endian order, 4 bytes). However, the server needs to implement a corresponding packet unpacking process. During my test of client-side data receiving, no chaotic byte reading, missing bytes, or packet sticking issues occurred (probably because an asynchronous lock was added when reading bytes from the stream). Therefore, there is no packet unpacking process in my RecvMsgAsync method. If users encounter such issues, they can send me an email or leave a message on GitHub.



Email: zzz136479@qq.com

