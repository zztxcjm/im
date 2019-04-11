using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;

using FaceHand.Common.Exceptions;
using FaceHand.Common.Util;
using StackExchange.Redis;
using Newtonsoft.Json;
using IMServices3.Util;
using IMServices3.Entity;
using IMServices3.DataAccessor;

namespace IMServices3.Controllers
{
    public class MessageController : BaseController
    {

        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msgtype">消息类型</param>
        /// <param name="receivers">消息接收者</param>
        /// <param name="msgbody">消息体</param>
        /// <param name="extinfo">扩展信息</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult Send(string accesstoken, MsgType msgtype, string receivers, string msgbody, string extinfo)
        {

            //验证发送者
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);
            var userinfo = IMRedisDAL.GetUserInfo(userid);

            //接收人不能为空
            if (String.IsNullOrEmpty(receivers))
                throw new BusinessException("消息接收人不能为空");

            //验证消息体完整性
            ValidateMsgBody(msgtype, msgbody);

            //将消息放入消息缓存，放入消息缓存的目的是为了防止消息在发送失败时可以进行多次尝试
            var msg_uid = Guid.NewGuid().ToString();
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGCACHE_KEY);
            redis.Exec(db =>
            {
                var key = $"msg_{msg_uid}";
                db.HashSet(key, new HashEntry[] {
                    new HashEntry(RedisFields.MSG_UID, msg_uid),
                    new HashEntry(RedisFields.MSG_TYPE, (int)msgtype),
                    new HashEntry(RedisFields.MSG_SEND_USERTYPE, Convert.ToInt32(userinfo[RedisFields.USER_TYPE])),
                    new HashEntry(RedisFields.MSG_SEND_USERID, userid),
                    new HashEntry(RedisFields.MSG_RECEIVERS, receivers.NullDefault()),
                    new HashEntry(RedisFields.MSG_BODY, msgbody.NullDefault()),
                    new HashEntry(RedisFields.MSG_TRYCOUNT, 0),
                    new HashEntry(RedisFields.MSG_TRYTIME, 0),
                    new HashEntry(RedisFields.MSG_STATE, (byte)MsgSendState.Sending),
                    new HashEntry(RedisFields.MSG_SEND_TIME, DateTime.Now.AsUnixTimestamp()),
                    new HashEntry(RedisFields.USER_EXT_INFO, extinfo.NullDefault())
                });

            });

            //通知发送新消息
            redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGMQ_KEY);
            redis.Exec(db => {

                var batch = db.CreateBatch();
                //放入待发送队列
                batch.ListLeftPushAsync(MessageSendStateQueue.SENDING, msg_uid, When.Always, CommandFlags.FireAndForget);
                //通过订阅通知发送器异步处理发送
                batch.PublishAsync(RedisChannelName.NEW_MESSAGE_CHANNEL_TOPULLER, ConstDefined.PUBCMD_SEND, CommandFlags.FireAndForget);

                batch.Execute();

            });

            //返回消息ID
            return JsonContent(new { msgid = msg_uid });

        }

        #region 发送消息私有方法

        private void ValidateMsgBody(MsgType msgtype, string msgbody)
        {

            if (String.IsNullOrEmpty(msgbody))
            {
                throw new BusinessException("消息体不能为空");
            }

            switch (msgtype)
            {
                case MsgType.chat_text:
                    {
                        var textBody = Newtonsoft.Json.JsonConvert.DeserializeObject<TextMessageBody>(msgbody);
                        if (textBody == null || String.IsNullOrEmpty(textBody.text))
                        {
                            throw new BusinessException("文字消息不能为空");
                        }
                        if(textBody.text.Length>1000)
                        {
                            throw new BusinessException("文字消息内容长度不能超过1000个字");
                        }
                        break;
                    }
                case MsgType.chat_pictures:
                    {

                        var picBody = Newtonsoft.Json.JsonConvert.DeserializeObject<PictureMessageBody>(msgbody);
                        if (picBody == null || picBody.pictures == null || picBody.pictures.Count() == 0)
                        {
                            throw new BusinessException("图片消息至少应该包含1张图片");
                        }
                        foreach (var pic in picBody.pictures)
                        {
                            if (String.IsNullOrEmpty(pic.orgUrl))
                            {
                                throw new BusinessException("图片原始地址不能为空");
                            }
                            if (string.IsNullOrEmpty(pic.thubUrl))
                            {
                                throw new BusinessException("图片缩略图地址不能为空");
                            }
                        }

                        break;

                    }
                case MsgType.chat_welcome:
                    {
                        var body = Newtonsoft.Json.JsonConvert.DeserializeObject<WelcomeMessageBody>(msgbody);
                        if (body == null || String.IsNullOrEmpty(body.text))
                        {
                            throw new BusinessException("欢迎消息不能为空");
                        }
                        if (body.text.Length > 1000)
                        {
                            throw new BusinessException("欢迎消息内容长度不能超过1000个字");
                        }
                        break;
                    }
                case MsgType.system_app:
                    break;
                case MsgType.system_cmd:
                    break;
                case MsgType.system_push:
                    {
                        var body = Newtonsoft.Json.JsonConvert.DeserializeObject<SystemPushBody>(msgbody);
                        if (body == null || String.IsNullOrEmpty(body.type) || String.IsNullOrEmpty(body.data))
                        {
                            throw new BusinessException("系统推送消息Type和Data不能为空");
                        }
                        break;
                    }
                default:
                    throw new BusinessException("暂不支持此消息类型");
            }

        }

        #endregion

        /// <summary>
        /// 获取消息发送的状态
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msgid">消息ID</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetSendState(string accesstoken, string msgid)
        {
            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            //获取消息
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGCACHE_KEY);
            var dict = redis.Exec<Dictionary<string, string>>(db => db.HashGetAll($"msg_{msgid}").AsDictionary());

            if (dict == null || dict.Count == 0)
            {
                throw new BusinessException("消息未找到");
            }

            return JsonContent(new
            {
                state = dict.GetStringValue(RedisFields.MSG_STATE).AsInt(),
                errormsg = dict.GetStringValue(RedisFields.MSG_ERROR_MSG),
                trycount = dict.GetStringValue(RedisFields.MSG_TRYCOUNT).AsInt(),
                trytime = dict.GetStringValue(RedisFields.MSG_TRYTIME).AsLong(),
                receivers_success = dict.GetStringValue(RedisFields.MSG_SUCCESS_RECEIVERS),
                receivers_failed = dict.GetStringValue(RedisFields.MSG_FAILED_RECEIVERS)
            });

        }

        /// <summary>
        /// 获取消息
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msgid">消息ID</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetMessage(string accesstoken, string msgid)
        {
            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            //获取消息
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGCACHE_KEY);
            var dict = redis.Exec<Dictionary<string, string>>(db => db.HashGetAll($"msg_{msgid}").AsDictionary());

            if (dict == null || dict.Count == 0)
            {
                throw new BusinessException("消息未找到");
            }

            return JsonContent(dict.BuildMsg());

        }

        /// <summary>
        /// 获取聊天消息的列表
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="chatuserid">聊天对象</param>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">分页尺寸（不能小于5）</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetChatList(string accesstoken, string chatuserid, int pageIndex, int pageSize)
        {
            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            if (String.IsNullOrEmpty(chatuserid))
            {
                throw new BusinessException("chatuserid不能为空");
            }

            pageIndex = Math.Max(1, pageIndex);
            pageSize = Math.Max(5, pageSize);

            var dt = IMMySqlDAL.Instance.SelectChatMessageList(userid, chatuserid, pageIndex, pageSize);
            var re = new List<dynamic>();
            foreach (DataRow row in dt.Rows)
            {
                re.Add(row.BuildMsg());
            }

            return JsonContent(re);

        }

        /// <summary>
        /// 获取聊天消息的未读消息数
        /// </summary>
        /// <param name="accesstoken"></param>
        /// <param name="chatuserids"></param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetChatNotReadCount(string accesstoken, string chatuserids)
        {

            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            if (String.IsNullOrEmpty(chatuserids))
                throw new BusinessException("chatuserids不能为空");

            var dict = new Dictionary<string, int>();
            var user_list = chatuserids.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (user_list.Length > 0)
            {
                var redis_msgpost = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
                foreach (var r in user_list)
                {
                    //我发送的
                    //var k1 = $"chatnotreadcount_{userid}_{r}";
                    //我接收的
                    var k2 = $"chatnotreadcount_{r}_{userid}";
                    var count = redis_msgpost.Exec<int>(db => {

                        var count1 = 0;
                        var count2 = 0;

                        //var k1v = db.Execute("get", k1);
                        //if (k1v.HasValue() && !k1v.IsNull && !k1v.IsEmpty())
                        //    count1 = Convert.ToInt32(k1v.ToString());

                        var k2v = db.Execute("get", k2);
                        if (k2v.HasValue() && !k2v.IsNull && !k2v.IsEmpty())
                            count2 = Convert.ToInt32(k2v.ToString());

                        return Math.Max(0, count1 + count2);

                    });

                    dict.Add(r, count);

                }
            }

            return JsonContent(dict);


        }
        
        /// <summary>
        /// 获取未读消息列表
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msgtypes">限制消息类型，默认是ALL</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetNotReadList(string accesstoken, string msgtypes)
        {

            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            var notReadMsgList = new List<dynamic>();

            //从未读消息列表中取出未读消息的ID
            var notread_msg_uid_arr = FaceHand.Common.ResdisExecutor.ExecCommand<RedisValue[]>(
                db => db.SortedSetRangeByRank($"notreadlist_{userid}", 0, -1), RedisConfig.REDIS_MSGPOST_KEY);

            if (notread_msg_uid_arr!=null && notread_msg_uid_arr.Length>0)
            {

                var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGCACHE_KEY);

                IEnumerable<int> msgtype_list = null;
                if (!String.IsNullOrEmpty(msgtypes))
                {
                    msgtype_list = msgtypes.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => {
                        int t;
                        return int.TryParse(item, out t) ? t : (int)item.AsEnum<MsgType>();
                    });
                }


                foreach (var msg_uid in notread_msg_uid_arr)
                {
                    redis.Exec(db => {

                        var msg = db.HashGetAll($"msg_{msg_uid}");
                        if(msg==null || msg.Length==0)
                        {
                            return;
                        }

                        var msg_dict = msg.AsDictionary();
                        if (msgtype_list == null)
                        {
                            notReadMsgList.Add(msg_dict.BuildMsg());
                        }
                        else
                        {
                            if (msgtype_list.Contains(msg_dict.GetStringValue(RedisFields.MSG_TYPE).AsInt()))
                            {
                                notReadMsgList.Add(msg_dict.BuildMsg());
                            }
                        }

                    });
                }

            }

            return JsonContent(notReadMsgList);

        }

        /// <summary>
        /// 获取未读消息条数
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msgtypes">限制消息类型，默认是ALL</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetNotReadCount(string accesstoken, string msgtypes)
        {
            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            var redis_msgpost = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
            var count = redis_msgpost.Exec<int>(db=> {

                var lst = db.HashGetAll($"notreadcount_{userid}");
                if (lst == null || lst.Length == 0)
                    return 0;

                int re = 0;
                if (String.IsNullOrEmpty(msgtypes))
                {
                    foreach (HashEntry i in lst)
                        re += i.HasValue() ? Convert.ToInt32(i.Value) : 0;
                }
                else
                {
                    var msgtypes_lst = msgtypes.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => {
                            int t;
                            return int.TryParse(item, out t) ? t : (int)item.AsEnum<MsgType>();
                        });

                    foreach (HashEntry i in lst)
                        re += (i.HasValue() && msgtypes_lst.Contains(Convert.ToInt32(i.Name.ToString())))
                            ? Convert.ToInt32(i.Value) : 0;
                }

                return re;

            });

            return JsonContent<int>(Math.Max(count, 0));

        }

        /// <summary>
        /// 获取阅读状态
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msg_uids">消息ID列表</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult GetReadState(string accesstoken, string msg_uids)
        {

            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            var readstate = new Dictionary<string, dynamic>();
            if (!String.IsNullOrEmpty(msg_uids))
            {
                var msg_uid_list = msg_uids.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var redis_msgpost = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
                redis_msgpost.Exec(db => {

                    foreach (var msg_uid in msg_uid_list)
                    {
                        var val = db.SortedSetScore($"readtime_{msg_uid}", userid);
                        if (val.HasValue)
                        {
                            readstate.Add(msg_uid.ToString(), new { readstate = 1, readtime = val.Value });
                        }
                        else
                        {
                            readstate.Add(msg_uid.ToString(), new { readstate = 0 });
                        }
                    }

                });

            }

            return JsonContent(readstate);

        }

        /// <summary>
        /// 更新消息阅读状态
        /// </summary>
        /// <param name="accesstoken">访问者的accesstoken</param>
        /// <param name="msg_uids">要更新的消息ID列表</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult UpdateReadState(string accesstoken, string msg_uids)
        {

            //先验证访问权限
            var userid = IMRedisDAL.GetUserIdByAccessToken(accesstoken);

            if (!String.IsNullOrEmpty(msg_uids))
            {
                var msg_uid_list = msg_uids.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                var redis_msgpost = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
                redis_msgpost.Exec(db=> {

                    var chatMsgList = new List<Dictionary<string, string>>();

                    //更新整体的未读消息数量
                    var batch = db.CreateBatch();
                    foreach (string msg_uid in msg_uid_list)
                    {
                        var readtime = db.SortedSetScore($"readtime_{msg_uid}", userid);
                        if (!readtime.HasValue)
                        {
                            //从未读消息列表中移除
                            batch.SortedSetRemoveAsync($"notreadlist_{userid}", msg_uid, CommandFlags.FireAndForget);

                            //更新未读消息统计计数
                            var msg = IMRedisDAL.GetMsg(msg_uid);
                            var msgtype_str = msg.GetStringValue(RedisFields.MSG_TYPE);
                            if (!String.IsNullOrEmpty(msgtype_str))
                            {
                                var count_rv = db.HashGet($"notreadcount_{userid}", msgtype_str);
                                if (count_rv.HasValue && !count_rv.IsNullOrEmpty)
                                {
                                    long count_lng = 0;
                                    if (count_rv.TryParse(out count_lng))
                                    {
                                        if (count_lng > 0)
                                        {
                                            batch.HashDecrementAsync($"notreadcount_{userid}", msgtype_str, 1, CommandFlags.FireAndForget);
                                        }
                                        else
                                        {
                                            batch.HashSetAsync($"notreadcount_{userid}", msgtype_str, 0);
                                        }
                                    }
                                }

                                //如果是聊天消息，更新聊天消息的阅读情况
                                var msgtype_int = Convert.ToInt32(msgtype_str);
                                if (msgtype_int < 60) chatMsgList.Add(msg);

                            }

                            //更新阅读时间
                            batch.SortedSetAddAsync($"readtime_{msg_uid}",
                                new SortedSetEntry[] { new SortedSetEntry(userid, DateTime.Now.AsUnixTimestamp()) }, CommandFlags.FireAndForget);

                        }

                    }

                    batch.Execute();

                    //更新聊天消息和接收者之间的的未读数量
                    if (chatMsgList.Count > 0)
                    {
                        var batch2 = db.CreateBatch();
                        foreach (var msg in chatMsgList)
                        {
                            var msg_senderid = msg.GetStringValue(RedisFields.MSG_SEND_USERID);
                            var receivers = msg.GetStringValue(RedisFields.MSG_SUCCESS_RECEIVERS);
                            if (!String.IsNullOrEmpty(receivers))
                            {
                                var receivers_list = receivers.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var receiver_userid in receivers_list)
                                {
                                    var k = $"chatnotreadcount_{msg_senderid}_{receiver_userid}";
                                    var count_rv = db.Execute("get", k);
                                    if (count_rv.HasValue() && !count_rv.IsNull && !count_rv.IsEmpty())
                                    {
                                        //这个key必须大于0才执行递减操作
                                        //否则不变
                                        if (count_rv.AsInt() > 0)
                                        {
                                            batch2.ExecuteAsync("DECRBY", k, 1);
                                        }
                                        else
                                        {
                                            batch2.ExecuteAsync("set", k, 0);
                                        }
                                    }
                                    else
                                    {
                                        //这个key还不存在
                                        batch2.ExecuteAsync("set", k, 0);
                                    }
                                }
                            }
                        }
                        batch2.Execute();
                    }

                });

            }

            return JsonContent(true);

        }

    }
}