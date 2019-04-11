using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Text.RegularExpressions;

using FaceHand.Common.Util;
using FaceHand.Common.Exceptions;
using StackExchange.Redis;

using IMServices3.Util;
using IMServices3.Entity;
using IMServices3.DataAccessor;

namespace IMServices3.Controllers
{
    public class ClientController : BaseController
    {       
        public class WSServerInfo
        {
            public int ServerId { get; set; }
            public int ClientCount { get; set; }
            public DateTime LastUpdateTime { get; set; }
        }

        public class WSServerInfoComparer : IComparer<WSServerInfo>
        {
            public int Compare(WSServerInfo x, WSServerInfo y)
            {

                if (x.ServerId == y.ServerId)
                    return 0;

                if (x.ClientCount > y.ClientCount)
                    return 1;
                if (x.LastUpdateTime > y.LastUpdateTime)
                    return 1;

                return -1;

            }
        }

        /// <summary>
        /// 获取WebSocketServer地址
        /// </summary>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetWebSocketServer(bool ssl = false)
        {

            //强制启用SSL
            ssl = true;

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_STATE_KEY);
            var url = redis.Exec<string>(db =>
            {

                var data = db.HashGetAll("websocketserver_clientcount");
                if (data == null || data.Length == 0)
                {
                    var defaultws = FaceHand.Common.AppSetting.Default.GetItem("Default_WS");
                    if (ssl)
                    {
                        if (!defaultws.StartsWith("wss:"))
                            defaultws = String.Empty;
                    }
                    return defaultws;
                }
                else
                {
                    //默认的单个WS节点连接数
                    var WS_ClientCountLimit_int = 0;
                    var WS_ClientCountLimit_string = FaceHand.Common.AppSetting.Default.GetItem("WS_ClientCountLimit");
                    if (String.IsNullOrEmpty(WS_ClientCountLimit_string))
                    {
                        WS_ClientCountLimit_int = 10000;
                    }
                    else
                    {
                        WS_ClientCountLimit_int = Convert.ToInt32(WS_ClientCountLimit_string);
                    }

                    //按照连接数从大到小排列
                    var serverList = data.Select(item => {
                        var v = item.Value.ToString().Split(',');
                        return new WSServerInfo()
                        {
                            ServerId = Convert.ToInt32(item.Name.ToString()),
                            ClientCount = Convert.ToInt32(v[0]),
                            LastUpdateTime = v[1].AsDateTimeFromUnixTimestamp()
                        };
                    }).OrderByDescending(item=>item, new WSServerInfoComparer());

                    var now = DateTime.Now;
                    foreach (var server in serverList)
                    {
                        //客户端连接数小于最大限制，同时最后更新时间在60秒内
                        if (server.ClientCount < WS_ClientCountLimit_int
                            && (now - server.LastUpdateTime).TotalSeconds <= 60)
                        {
                            var re = FaceHand.Common.AppSetting.Default.GetItem("WS_ServerID_" + server.ServerId);
                            if (ssl)
                            {
                                if (!re.StartsWith("wss:"))
                                    continue;
                            }
                            return re;
                        }
                    }

                    var defaultws = FaceHand.Common.AppSetting.Default.GetItem("Default_WS");
                    if (ssl)
                    {
                        if (!defaultws.StartsWith("wss:"))
                            defaultws = String.Empty;
                    }

                    return defaultws;

                }

            });

            if (String.IsNullOrEmpty(url))
            {
                throw new BusinessException("未找到WS服务器地址");
            }

            return JsonContent(url);

        }

        /// <summary>
        /// 获取客户端UI状态
        /// </summary>
        /// <param name="userid"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetClientUiState(string userid)
        {
            if (String.IsNullOrEmpty(userid))
                throw new BusinessException("userid不能为空");

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            var re = redis.Exec<int>(db => db.Execute("get", $"imuser_user_clientstate_{userid}").AsInt());

            return JsonContent<string>(re == 1 ? "visible" : "hidden");

        }

        /// <summary>
        /// 更新客户端的UI状态
        /// </summary>
        /// <param name="userid"></param>
        /// <param name="state"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult UpdateClientUiState(string userid, string state)
        {

            if (String.IsNullOrEmpty(userid))
                throw new BusinessException("userid不能为空");
            if (String.IsNullOrEmpty(state))
                throw new BusinessException("state不能为空");

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            redis.Exec(db => db.Execute("set", $"imuser_user_clientstate_{userid}", state == "visible" ? 1 : 0));

            return JsonContent<bool>(true);

        }

        /// <summary>
        /// 显示请求的request头
        /// </summary>
        /// <returns></returns>
        public ActionResult ShowHttpHeader()
        {
            foreach(var k in Request.Headers.AllKeys)
            {
                Response.Write(k);
                Response.Write(":");
                Response.Write(Request.Headers[k]);
                Response.Write("</br>");
            }
            return null;
        }

    }
}