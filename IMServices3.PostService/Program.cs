using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using FaceHand.Common;
using FaceHand.Common.Util;
using FaceHand.Common.Exceptions;

using StackExchange.Redis;

using IMServices3.Util;
using IMServices3.Entity;
using IMServices3.DataAccessor;

namespace IMServices3.PostService
{
    class Program
    {

        //实例ID
        private static int? _instanceId = null;

        //全局定时器
        private static Timer _globalTimer = null;

        //消息订阅器
        private static ISubscriber _globalSubscriber = null;

        //运行模式
        private static RunMode _runmode = RunMode.Master;

        //子进程消息订阅通道的最后活动时间
        private static DateTime _slave_subscriber_lastActiveTime = DateTime.Now;

        //已启动的子进程
        private static List<Process> _slave_process = new List<Process>();

        //启动子进程个数
        private static int _slave_process_childcount = 6;

        public static void Main(string[] args)
        {

            //启动参数处理
            if (args != null)
            {
                var argLen = args.Length;
                if (argLen > 0)
                {
                    if (argLen % 2 != 0)
                    {
                        WriteErrorLog("boot argument number error");
                        Console.ReadLine();
                        return;
                    }
                    for (int i = 0; i < argLen; i = i + 2)
                    {
                        var cmd = args[i];
                        var val = args[i + 1].Trim();
                        switch (cmd)
                        {
                            case "-mode":
                                {
                                    val = val.ToLower();
                                    _runmode = val == "slave" ? RunMode.Slave : RunMode.Master;
                                    break;
                                }
                            case "-count":
                                {
                                    int.TryParse(val, out _slave_process_childcount);
                                    break;
                                }
                            case "-id":
                                {
                                    int sid = 0;
                                    if (!int.TryParse(val, out sid))
                                    {
                                        WriteErrorLog("argument -id must be a number");
                                        Console.ReadLine();
                                        return;
                                    }
                                    else
                                    {
                                        _instanceId = sid;
                                    }
                                    break;
                                }
                            case "-log":
                                {
                                    if (!String.IsNullOrEmpty(val))
                                    {
                                        logfile = val.Trim();
                                    }
                                    break;
                                }
                        }

                    }
                }
            }

            if (_runmode == RunMode.Master)
            {

                MasterMode();

                //直到输入命令q时退出
                while (true)
                {
                    if (Console.ReadLine() == "q")
                    {
                        break;
                    }
                }

                //退出所有进程
                if (_slave_process != null)
                {
                    foreach (var p in _slave_process)
                    {
                        try
                        {
                            p.Close();
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

            }
            else if (_runmode == RunMode.Slave)
            {
                SlaveMode();
            }
            else
            {
                WriteErrorLog("-mode not invalid");
            }

        }

        private static void MasterMode()
        {
            for (int id = 0; id < _slave_process_childcount; id++)
            {
                var psi = new System.Diagnostics.ProcessStartInfo("IMServices3.PostService.exe");
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.Arguments = $"-mode slave -id {id}";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.UseShellExecute = false;
                var p = Process.Start(psi);

                _slave_process.Add(p);

                p.OutputDataReceived += P_OutputDataReceived;
                p.ErrorDataReceived += P_ErrorDataReceived;

                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

            }

            //启动一个定时器检查进程和订阅器是否还在线
            new Timer(
                state => NotifyChannelStateCheck_Master(),
                null,
                0,             //立即启动定时器
                1000 * 5       //5秒检查一次进程状态
                );

        }

        private static void P_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine(e.Data);
            }
        }

        private static void P_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!String.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine(e.Data);
            }
        }

        private static void SlaveMode()
        {
            if (!_instanceId.HasValue)
            {
                _instanceId = 0;
            }

            //定时保存日志
            if (enabledLogFile)
            {
                _globalTimer = new Timer(
                    state => SaveLogToFile(),
                    null,
                    0,              //立即启动定时器
                    1000 * 60       //1分钟保存一次日志
                    );
            }

            //检查一下是否有历史的待发送消息
            var redis_msgmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
            var msgcount = redis_msgmq.Exec<long>(db => db.ListLength(MessageSendStateQueue.SENDING));
            if (msgcount > 0)
            {
                //发送历史待处理消息
                Task.Factory.StartNew(ProcessHistoryMessage);
            }

            //启动订阅通知接收器，以便能即时获知有新消息到来
            try
            {
                _globalSubscriber = FaceHand.Common.ResdisExecutor.GetConn(RedisConfig.REDIS_MSGMQ_KEY).GetSubscriber();
                _globalSubscriber.Subscribe(RedisChannelName.NEW_MESSAGE_CHANNEL_TOPULLER, NotifyChannelHandler);

                WriteLog($"PostService Instance {_instanceId.Value} Is Started");

                new Timer(
                    state => NotifyChannelStateCheck_Slave(),
                    null,
                    0,             //立即启动定时器
                    1000 * 5       //5秒检测一次推送通道是否还存活，如果没有存活就尝试重启当前进程
                    );

            }
            catch (Exception ex)
            {
                WriteErrorLog("PostService Boot Failed." + ex.Message);
            }

            Console.ReadLine();

            //
            if (_globalTimer != null)
                _globalTimer.Dispose();
            if (_globalSubscriber != null)
                _globalSubscriber.UnsubscribeAll();
        }

        private static void NotifyChannelStateCheck_Master()
        {
            //检查子进程是否还存在
            if (_slave_process != null && _slave_process.Count > 0)
            {
                foreach (var p in _slave_process)
                {
                    if (p.HasExited)
                    {
                        p.Start();
                    }
                }
            }

            //检查订阅器是否还在线
            if (_globalSubscriber == null)
            {
                _globalSubscriber = FaceHand.Common.ResdisExecutor.GetConn(RedisConfig.REDIS_MSGMQ_KEY).GetSubscriber();
            }

            _globalSubscriber.PublishAsync(
                RedisChannelName.NEW_MESSAGE_CHANNEL_TOPULLER,
                ConstDefined.PUBCMD_AREYOUOK,
                CommandFlags.FireAndForget);

        }

        private static void NotifyChannelStateCheck_Slave()
        {
            //超过60秒没有收到心跳消息，就退出进程，主进程会自动重启
            if ((DateTime.Now - _slave_subscriber_lastActiveTime).TotalSeconds >= 60)
            {
                Process.GetCurrentProcess().Kill();
            }

        }

        private static void NotifyChannelHandler(RedisChannel channel, RedisValue message)
        {

            //WriteLog($"{channel}: {message}");

            if (_globalSubscriber != null)
            {
                switch(message)
                {
                    case ConstDefined.PUBCMD_CLOSE:
                        {
                            try
                            {
                                _globalSubscriber.Unsubscribe(channel);
                            }
                            catch (Exception ex)
                            {
                                WriteErrorLog($"unsubscribe failed. {ex.Message}");
                            }
                            break;
                        }
                    case ConstDefined.PUBCMD_CLOSEALL:
                        {
                            try
                            {
                                _globalSubscriber.UnsubscribeAll();
                            }
                            catch (Exception ex)
                            {
                                WriteErrorLog($"unsubscribe failed. {ex.Message}");
                            }
                            break;
                        }
                    case ConstDefined.PUBCMD_AREYOUOK:
                        {
                            _slave_subscriber_lastActiveTime = DateTime.Now;
                            //WriteLog("AreYouOk Messages");
                            break;
                        }
                    case ConstDefined.PUBCMD_SEND:
                        {
                            try
                            {
                                //从消息队列中取最后一下待发送消息
                                var redis_msmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
                                var msg_uid = redis_msmq.Exec<RedisValue>(db => db.ListRightPop(MessageSendStateQueue.SENDING));

                                if (msg_uid.HasValue && !msg_uid.IsNullOrEmpty)
                                {
                                    SendMsg(msg_uid, false);
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteErrorLog($"post message failed. {ex.Message}");
                            }
                            break;
                        }
                }
            }

        }

        //投送消息
        private static void SendMsg(string msg_uid, bool isRemoveFromMQ)
        {
            //获取消息以验证消息的有效性
            var msg = IMRedisDAL.GetMsg(msg_uid);
            if (msg == null || msg.Count == 0)
            {
                throw new BusinessException($"message {msg_uid} not found");
            }

            var state = msg.GetStringValue(RedisFields.MSG_STATE).AsEnum<MsgSendState>();
            if (state != MsgSendState.Sending)
            {
                //消息ID被取到，但是又不是正在发送的状态
                //说明消息已被其它进程处理，这时应该从消息队列中删除这个ID
                var redis_msmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
                redis_msmq.Exec(db => db.ListRemove(MessageSendStateQueue.SENDING, msg_uid, 0, CommandFlags.FireAndForget));
            }
            else
            {

                //验证接收人,把有效的人和无效的人分离出来
                var receiver_valid = new List<string>();
                var receiver_invalid = new List<string>();
                ValidateReceiver(msg[RedisFields.MSG_RECEIVERS], ref receiver_valid, ref receiver_invalid);

                //没有有效接收人，消息发送失败
                if (receiver_valid.Count == 0)
                {
                    //更新消息发送状态为失败
                    FaceHand.Common.ResdisExecutor.ExecCommand(db => db.HashSet($"msg_{msg_uid}", new HashEntry[] {

                        //更新发送失败的人
                        new HashEntry(RedisFields.MSG_FAILED_RECEIVERS,String.Join(",",receiver_invalid)),
                        //更新消息发送状态
                        new HashEntry(RedisFields.MSG_STATE,(int)MsgSendState.Failed),
                        //更新消息发送状态
                        new HashEntry(RedisFields.MSG_ERROR_MSG,"available receivers count is zero")

                    }, CommandFlags.FireAndForget), RedisConfig.REDIS_MSGCACHE_KEY);

                    //从队列中删除消息
                    if (isRemoveFromMQ)
                    {
                        var redis_msmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
                        redis_msmq.Exec(db => db.ListRemove(MessageSendStateQueue.SENDING, msg_uid, 0, CommandFlags.FireAndForget));
                    }

                    //抛出错误，以便控制台能看到发送失败
                    throw new BusinessException($"available receivers count is zero");

                }
                else
                {

                    //投送消息
                    var redis_msgpost = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
                    redis_msgpost.Exec(db => {

                        var batch = db.CreateBatch();
                        var msg_sendtime = msg.GetStringValue(RedisFields.MSG_SEND_TIME).AsLong();
                        var msg_senderid = msg.GetStringValue(RedisFields.MSG_SEND_USERID);
                        var msg_type = (int)msg.GetStringValue(RedisFields.MSG_TYPE).AsEnum<MsgType>();
                        var isChatMsg = msg_type < 60;
                        var now = DateTime.Now;
                        var nowUnixTimestmp = now.AsUnixTimestamp();
                        foreach (var receiver_userid in receiver_valid)
                        {

                            //更新未读消息数，未读消息数按消息类型分别存
                            batch.HashIncrementAsync($"notreadcount_{receiver_userid}", msg[RedisFields.MSG_TYPE], 1D, CommandFlags.FireAndForget);

                            //将消息ID放入对应人的未读消息清单
                            batch.SortedSetAddAsync($"notreadlist_{receiver_userid}", msg_uid, nowUnixTimestmp, When.Always, CommandFlags.FireAndForget);

                            //单独统计聊天消息的数量
                            if (isChatMsg)
                            {
                                var chatnotreadcount_k = $"chatnotreadcount_{msg_senderid}_{receiver_userid}";
                                var chatnotreadcount = db.Execute("GET", chatnotreadcount_k).AsInt();

                                if (chatnotreadcount < 0)
                                    batch.ExecuteAsync("SET", 1);
                                else
                                    batch.ExecuteAsync("INCRBY", chatnotreadcount_k, 1D);

                            }

                        }

                        batch.Execute();

                    });

                    //更新消息发送状态
                    FaceHand.Common.ResdisExecutor.ExecCommand(db => db.HashSet($"msg_{msg_uid}", new HashEntry[] {

                        //更新发送成功的人
                        new HashEntry(RedisFields.MSG_SUCCESS_RECEIVERS,String.Join(",",receiver_valid)),

                        //更新发送失败的人
                        new HashEntry(RedisFields.MSG_FAILED_RECEIVERS,String.Join(",",receiver_invalid)),

                        //更新消息发送状态
                        new HashEntry(RedisFields.MSG_STATE,(int)MsgSendState.Success)

                    }, CommandFlags.FireAndForget), RedisConfig.REDIS_MSGCACHE_KEY);

                    var redis_msmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);

                    //从队列中删除消息
                    if (isRemoveFromMQ)
                    {
                        redis_msmq.Exec(db => db.ListRemove(MessageSendStateQueue.SENDING, msg_uid, 0, CommandFlags.FireAndForget));
                    }

                    //通过订阅器通知客户端有新消息
                    if (receiver_valid.Count > 0)
                    {
                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                redis_msmq.Exec(db => db.Publish(
                                    RedisChannelName.NEW_MESSAGE_CHANNEL_TOCLIENT,
                                    $"{msg_uid},{String.Join(",", receiver_valid)}", //通知client manager
                                    CommandFlags.FireAndForget));
                            }
                            catch (Exception ex)
                            {
                                WriteErrorLog($"notify {msg_uid} to client failed. {ex.Message}");
                            }
                        });
                    }

                    //将消息持久化到数据库
                    Task.Factory.StartNew(() =>
                    {
                        try
                        {
                            using (var context = new DataContext(MysqlDbConfig.IMCONN))
                            {
                                IMMySqlDAL.Instance.InsertMsg(msg, receiver_valid);
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteErrorLog($"save message {msg_uid} to database failed. {ex.Message}");
                        }
                    });

                    //如果是应用消息，还要考虑是否通知到企业微信端
                    if (msg[RedisFields.MSG_TYPE].AsEnum<MsgType>() == MsgType.system_app)
                    {
                    }

                    WriteLog($"post message success。msg_uid:{msg_uid}");

                }

            }

        }

        //处理历史消息
        private static void ProcessHistoryMessage()
        {
            var redis_msgmq = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
            var msguid_list = redis_msgmq.Exec<RedisValue[]>(db => db.ListRange(MessageSendStateQueue.SENDING, 0, -1));

            foreach (RedisValue msg_uid in msguid_list)
            {
                if (!msg_uid.IsNullOrEmpty)
                {
                    try
                    {
                        SendMsg(msg_uid, true);
                    }
                    catch (Exception ex)
                    {
                        WriteErrorLog($"post message failed. {ex.Message}");
                    }
                }
            }

        }

        //验证接收者
        private static void ValidateReceiver(string receivers, ref List<string> receiver_valid, ref List<string> receiver_invalid)
        {
            if (receiver_valid == null)
                receiver_valid = new List<string>();
            if (receiver_invalid == null)
                receiver_invalid = new List<string>();

            if (String.IsNullOrEmpty(receivers))
                return;

            var receivers_all = receivers.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var redis_userinfo = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_USER_KEY);
            foreach (var userid in receivers_all)
            {
                if (redis_userinfo.Exec<bool>(db => db.KeyExists($"im_userinfo_{userid}")))
                    receiver_valid.Add(userid);
                else
                    receiver_invalid.Add(userid);
            }

        }

        #region 日志

        private static StringBuilder _logBuffer = new StringBuilder();
        private static DateTime? _lastWriteLogTime = DateTime.Now;

        //日志相关
        private static bool enabledLogFile = true;
        private static string log_folder = "logs";
        private static string logfile = "post.log";

        private static string GetLogFilePath()
        {
            var tmpfilename = _instanceId.HasValue
                ? _instanceId.Value + "_" + logfile
                : logfile;

            var re = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, log_folder, tmpfilename);
            var log_folder_fullpath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, log_folder);
            if (!Directory.Exists(log_folder_fullpath))
            {
                Directory.CreateDirectory(log_folder_fullpath);
            }

            return re;
        }

        private static void WriteLog(string log, bool isErrorLog = false)
        {

            if (String.IsNullOrEmpty(log))
                return;

            var now = DateTime.Now;
            var serverId = _instanceId.HasValue ? $"【{_instanceId.Value}】" : String.Empty;
            var logline = isErrorLog
                ? $"{now.ToString()} -{serverId}【ERROR】{log}"
                : $"{now.ToString()} -{serverId}【INFO】{log}";

            //打印到屏幕
            Console.WriteLine(logline);

            //写日志到缓存，以便后续保存到文件
            if (enabledLogFile)
            {
                _logBuffer.AppendLine(logline);
            }

        }

        private static void WriteErrorLog(string log)
        {
            WriteLog(log, true);
        }

        private static void SaveLogToFile()
        {
            if (_logBuffer.Length > 0)
            {
                try
                {
                    var tmpBuffer = _logBuffer;
                    _logBuffer = new StringBuilder();
                    File.AppendAllText(GetLogFilePath(), tmpBuffer.ToString());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"write log failed。{ex.Message}");
                }
            }
        }

        #endregion

    }

    enum RunMode
    {
        Master,
        Slave
    }

}