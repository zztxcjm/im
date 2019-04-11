using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;

using FaceHand.Common.Exceptions;
using FaceHand.Common.Util;
using StackExchange.Redis;

using IMServices3.Util;
using IMServices3.Entity;
using IMServices3.DataAccessor;

namespace IMServices3.Controllers
{
    public class SessionController : BaseController
    {

        /// <summary>
        /// 使用账号和密码登录，如果登录成功会返回accesstoken
        /// </summary>
        /// <param name="userid"></param>
        /// <param name="pwd"></param>
        /// <param name="clientFlag"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult Login(string userid, string pwd, string clientFlag)
        {

            var userinfo = IMRedisDAL.GetUserInfo(userid);

            //验证登录密码
            if (userinfo.GetStringValue(RedisFields.USER_LOGIN_PWD) == pwd.Trim().GetSHA1HashCode())
            {

                var tsp = new TimeSpan(0, 2, 0, 0, 0);
                var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);

                //查找是否存在还未过期的token，如果存在就直接返回现在的token，就不创建新的token
                var imuser_usertoken = $"imuser_user_token_{userid}";
                var oldtoken = redis.Exec<dynamic>(db => {

                    var tokenlist = db.SetMembers(imuser_usertoken);
                    if (tokenlist != null && tokenlist.Length != 0)
                    {
                        foreach (var item in tokenlist)
                        {
                            var item_token = db.HashGetAll($"imuser_token_user_{item}").AsDictionary();
                            if (item_token.GetStringValue(RedisFields.SESSION_TOKEN_CLIENTFLAG).Equals(clientFlag, StringComparison.OrdinalIgnoreCase))
                            {
                                return new
                                {
                                    token = item.ToString(),
                                    expiry = item_token.GetStringValue(RedisFields.SESSION_TOKEN_CREATETIME).AsDateTimeFromUnixTimestamp().Add(tsp).AsUnixTimestamp()
                                };
                            }
                        }
                    }

                    return null;

                });

                if (oldtoken != null)
                {


                    //更新一下最后活动时间
                    var imuser_lastactivetime_userid = $"imuser_lastactivetime_{userid}";
                    redis.Exec(db => db.Execute("set", imuser_lastactivetime_userid, DateTime.Now.AsUnixTimestamp()));

                    return JsonContent(oldtoken);

                }
                else
                {
                    //创建新的token
                    var now = DateTime.Now;
                    var nowUnixTimestamp = now.AsUnixTimestamp();
                    var expiry = now.Add(tsp);
                    var token = $"{userid}{nowUnixTimestamp}{clientFlag}".GetSHA1HashCode();
                    var imuser_tokenuser = $"imuser_token_user_{token}";
                    var imuser_lastactivetime_userid = $"imuser_lastactivetime_{userid}";

                    redis.Exec(db => {

                        var batch = db.CreateBatch();

                        //保存token
                        batch.HashSetAsync(imuser_tokenuser, new HashEntry[] {
                            new HashEntry(RedisFields.SESSION_TOKEN_USERID, userid),
                            new HashEntry(RedisFields.SESSION_TOKEN_CLIENTFLAG, clientFlag),
                            new HashEntry(RedisFields.SESSION_TOKEN_CREATETIME, nowUnixTimestamp)
                        });
                        batch.KeyExpireAsync(imuser_tokenuser, expiry, CommandFlags.FireAndForget);

                        //将token添加到对应userid的集合里
                        batch.SetAddAsync(imuser_usertoken, token, CommandFlags.FireAndForget);

                        //lastactivetime
                        batch.ExecuteAsync("set", imuser_lastactivetime_userid, nowUnixTimestamp);

                        batch.Execute();

                    });

                    return JsonContent(new { token = token, expiry = expiry.AsUnixTimestamp() });

                }

            }

            throw new BusinessException("登录密码不正确", 10022);

        }

        /// <summary>
        /// 用于保持心跳和刷新最后活动时间，如果超过2分钟未活动，系统会认为登录已失效，所有分配的token也会同步失效
        /// </summary>
        /// <param name="accesstoken"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult KeepAlive(string accesstoken)
        {
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            redis.Exec(db => db.Execute("set", $"imuser_lastactivetime_{userid}", DateTime.Now.AsUnixTimestamp()));

            return JsonContent<bool>(true);
        }

        /// <summary>
        /// 根据AccessToken获取UserID
        /// </summary>
        /// <param name="accesstoken"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetUserId(string accesstoken)
        {
            var current_userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);
            return JsonContent<string>(current_userid);
        }

        /// <summary>
        /// 查询用户是否在线
        /// </summary>
        /// <param name="accesstoken"></param>
        /// <param name="userid"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetUserOnlineState(string accesstoken, string userid)
        {

            //var debuglog = new System.Text.StringBuilder();
            //debuglog.AppendLine($"datetime：{DateTime.Now.ToString()}");
            //debuglog.AppendLine($"accesstoken：{accesstoken}");
            //debuglog.AppendLine($"userid：{userid}");

            var isOnline = false;
            var current_userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            //debuglog.AppendLine($"current_userid：{current_userid}");

            var imuser_usertoken = $"imuser_user_token_{userid}";
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            var tokenList = redis.Exec<RedisValue[]>(db => db.SetMembers(imuser_usertoken));
            if (tokenList != null && tokenList.Length > 0)
            {

                //debuglog.AppendLine($"tokenList：{String.Join(",", tokenList.Select(item => item.ToString()))}");

                //能在有效的token中找到client，只能说明在2小时内登陆过，还不能证明是当前在线的，但这是证明在线一个前提
                var clientFlags = new List<string>() { ConstDefined.CLIENT_FLAG_WECHAT, ConstDefined.CLIENT_FLAG_MINIPROGRAM };
                var clientContain = false;

                foreach (var token in tokenList)
                {
                    var client = redis.Exec<string>(db => db.HashGet($"imuser_token_user_{token}", RedisFields.SESSION_TOKEN_CLIENTFLAG));
                    //debuglog.AppendLine($"{token}_client：{client}");
                    if (clientFlags.Contains(client))
                    {
                        //debuglog.AppendLine($"{token}_client_Contains：{client}");
                        clientContain = true;
                        break;
                    }
                }

                if (clientContain)
                {
                    //在判断最后一次活动时间，如果最后一次活动在5分钟以内就表示在新，否则表示不在线
                    var lasttime = redis.Exec<RedisResult>(db => db.Execute("get", $"imuser_lastactivetime_{userid}")).AsInt();
                    if (lasttime != 0 &&
                        (DateTime.Now - lasttime.AsDateTimeFromUnixTimestamp()).TotalSeconds <= Entity.ConstDefined.LASTACTIVETIME_MAX_SECONDS * 2)
                    {
                        isOnline = true;
                    }
                }

            }
            //System.IO.File.WriteAllText(Server.MapPath("~/1.txt"),debuglog.ToString());
            return JsonContent(isOnline);

        }

        /// <summary>
        /// 同时获取客户端的onlinestate和uistate，
        /// 用来取代接口/session/GetUserOnlineState和/client/GetClientUiState
        /// </summary>
        /// <param name="accesstoken"></param>
        /// <param name="userid"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetUserState(string accesstoken, string userid)
        {

            if (String.IsNullOrEmpty(userid))
                throw new BusinessException("userid不能为空");

            var isOnline = false;
            var uiState = "visible";
            var current_userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            var task1 = FaceHand.Common.ThreadHelp.Start(()=> {
                var imuser_usertoken = $"imuser_user_token_{userid}";
                var tokenList = redis.Exec<RedisValue[]>(db => db.SetMembers(imuser_usertoken));
                if (tokenList != null && tokenList.Length > 0)
                {
                    //能在有效的token中找到client，只能说明在2小时内登陆过，还不能证明是当前在线的，但这是证明在线一个前提
                    var clientFlags = new List<string>() { ConstDefined.CLIENT_FLAG_WECHAT, ConstDefined.CLIENT_FLAG_MINIPROGRAM };
                    var clientContain = false;

                    foreach (var token in tokenList)
                    {
                        var client = redis.Exec<string>(db => db.HashGet($"imuser_token_user_{token}", RedisFields.SESSION_TOKEN_CLIENTFLAG));
                        if (clientFlags.Contains(client))
                        {
                            clientContain = true;
                            break;
                        }
                    }

                    if (clientContain)
                    {
                        //在判断最后一次活动时间，如果最后一次活动在5分钟以内就表示在新，否则表示不在线
                        var lasttime = redis.Exec<RedisResult>(db => db.Execute("get", $"imuser_lastactivetime_{userid}")).AsInt();
                        if (lasttime != 0 &&
                            (DateTime.Now - lasttime.AsDateTimeFromUnixTimestamp()).TotalSeconds <= Entity.ConstDefined.LASTACTIVETIME_MAX_SECONDS * 2)
                        {
                            isOnline = true;
                        }
                    }
                }
            });
            var task2 = FaceHand.Common.ThreadHelp.Start(() => {
                var re = redis.Exec<int>(db => db.Execute("get", $"imuser_user_clientstate_{userid}").AsInt());
                uiState = (re == 1 ? "visible" : "hidden");
            });

            System.Threading.Tasks.Task.WaitAll(task1, task2);

            return JsonContent(new
            {
                online = isOnline,
                uistate = uiState
            });

        }

    }
}