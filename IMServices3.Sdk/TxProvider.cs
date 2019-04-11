using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FaceHand.Common.Util;
using FaceHand.Common.Exceptions;

using Newtonsoft.Json;

using IMServices3.Entity;
using IMServices3.Util;

namespace IMServices3.Sdk
{
    [Serializable]
    public class TxProvider
    {

        #region static

        public static TxProvider GetTxProvider(UserType userType, string userId, GetUserRegisterInfo cb = null)
        {

            if (String.IsNullOrEmpty(userId))
                throw new BusinessException("初始化TxProvider失败，userId不能为空");
            else
                userId = userId.Trim();

            //当前默认为123456
            var userPwd = "123456";

            TxProvider p;
            if (TryGetTxProvider(userId, out p))
                return p;
            else
            {
                //看用户是否注册
                if (UserIsExist(userId))
                    return BuildProvider(userId, userPwd);
                else
                {
                    //注册
                    var regErrorReason = String.Empty;
                    if (UserRegister(userType, userId, userPwd, cb, out regErrorReason))
                        return BuildProvider(userId, userPwd);
                    else
                        throw new BusinessException($"{userId}注册失败。(原因:{regErrorReason})");
                }
            }

        }

        private static string GetIMApiServer()
        {
            var url = FaceHand.Common.AppSetting.Default.GetItem("NewIMApiServer");
            if (String.IsNullOrEmpty(url))
                throw new BusinessException("IMApiServer不能为空,请检查配置文件");
            else
            {
                url = url.Trim();
                if (url.EndsWith("/"))
                    url = url.TrimEnd('/');
            }

            return url;

        }

        private static bool TryGetTxProvider(string userId, out TxProvider p)
        {
            p = null;

            var key = $"TxProvider_{userId}";
            var session = Common.MemcachedHelper.Instance.Get<UserLoginSessionInfo>(key);
            if (session == null)
                return false;
            else
            {
                if (DateTime.Now > FaceHand.Common.Util.DateTimeExt.FromUnixTimestamp(session.expiry).AddMinutes(-10))
                {
                    return false;
                }
                else
                {
                    p = new TxProvider(session.token);
                    return true;
                }

            }

        }

        private static bool UserIsExist(string userId)
        {
            try
            {
                var user = Common.WebApiHelper.Get<dynamic>(GetIMApiServer() + $"/user/get?userid={userId}", null);
                return true;
            }
            catch (BusinessException ex)
            {
                if (ex.ErrorCode == 10004)
                    return false;

                throw ex;

            }
        }

        private static bool UserRegister(UserType userType, string userId, string userPwd, GetUserRegisterInfo cb, out string regErrorReason)
        {

            regErrorReason = String.Empty;

            try
            {
                var param = new Dictionary<string, string>();
                param.Add("userType", userType.ToString());
                param.Add("userId", userId);
                param.Add("loginPwd", userPwd);

                if (cb != null)
                {
                    var reginfo = cb.Invoke(userType, userId);
                    if (reginfo == null)
                        throw new BusinessException("未能获取到注册信息，无法完成注册");

                    param.Add("userName", reginfo.UserName);
                    param.Add("sex", reginfo.Sex.ToString());
                    param.Add("faceUrl", reginfo.FaceUrl.NullDefault());
                    param.Add("extInfo", reginfo.ExtInfo.NullDefault());
                }
                else
                {
                    //如果不穿就自动生成一个名字
                    param.Add("userName", userId);
                    param.Add("sex", "0");
                    param.Add("faceUrl", String.Empty);
                    param.Add("extInfo", String.Empty);
                }

                return Common.WebApiHelper.Post<bool>(GetIMApiServer() + "/user/register", param.AsHttpParams());

            }
            catch (Exception ex)
            {
                regErrorReason = ex.Message;
                return false;
            }

        }

        private static TxProvider BuildProvider(string userId, string pwd)
        {
            //登录
            String logErrorReason = null;
            UserLoginSessionInfo session = null;

            if (UserLogin(userId, pwd, out session, out logErrorReason))
            {

                var p = new TxProvider(session.token);
                var key = $"TxProvider_{userId}";
                if (Common.MemcachedHelper.Instance.Get(key) == null)
                    Common.MemcachedHelper.Instance.Add(key, session);
                else
                {
                    Common.MemcachedHelper.Instance.Remove(key);
                    Common.MemcachedHelper.Instance.Add(key, session);
                }

                return p;

            }
            else
            {
                throw new BusinessException($"{userId}登录消息系统失败。(原因:{logErrorReason})");
            }
        }

        private static bool UserLogin(string userId, string pwd, out UserLoginSessionInfo session, out string logErrorReason)
        {

            logErrorReason = String.Empty;
            session = null;

            try
            {
                var param = new Dictionary<string, string>();
                param.Add("userid", userId);
                param.Add("pwd", pwd);
                param.Add("clientFlag", Entity.ConstDefined.CLIENT_FLAG_SERVER);

                session = Common.WebApiHelper.Post<UserLoginSessionInfo>(GetIMApiServer() + "/session/login", param.AsHttpParams());

                return true;

            }
            catch (BusinessException ex)
            {
                logErrorReason = ex.Message;
                return false;
            }

        }

        #endregion

        private string _imApiServer;
        private string _accessToken;

        private TxProvider(string accessToken)
        {
            this._imApiServer = GetIMApiServer();
            this._accessToken = accessToken;
        }

        public MsgSendResult SendChatMessage<T>(ChatMsgType msgtype, string receivers, T msgbody, string extinfo = null) where T : ChatMessageBody
        {

            if (String.IsNullOrEmpty(receivers))
                throw new BusinessException("消息接收人不能为空");

            var param = new Dictionary<string, string>();
            param.Add("accesstoken", _accessToken);
            param.Add("msgtype", ((int)msgtype).ToString());
            param.Add("msgbody", Newtonsoft.Json.JsonConvert.SerializeObject(msgbody));
            param.Add("receivers", receivers.Trim());
            param.Add("extinfo", extinfo);

            return Common.WebApiHelper.Post<MsgSendResult>(_imApiServer + "/message/send", param.AsHttpParams());

        }

        public MsgSendResult SendSystemPushMessage(string type, string data, string receivers)
        {

            if (String.IsNullOrEmpty(type))
                throw new BusinessException("推送消息类型不能为空");
            if (String.IsNullOrEmpty(data))
                throw new BusinessException("推送消息数据不能为空");
            if (String.IsNullOrEmpty(receivers))
                throw new BusinessException("消息接收人不能为空");

            var body = new SystemPushBody()
            {
                type = type,
                data = data
            };

            var param = new Dictionary<string, string>();
            param.Add("accesstoken", _accessToken);
            param.Add("msgtype", ((int)MsgType.system_push).ToString());
            param.Add("msgbody", Newtonsoft.Json.JsonConvert.SerializeObject(body));
            param.Add("receivers", receivers.Trim());

            return Common.WebApiHelper.Post<MsgSendResult>(_imApiServer + "/message/send", param.AsHttpParams());

        }

        public bool UserIsOnline(string userid)
        {

            var param = new Dictionary<string, string>();
            param.Add("accesstoken", _accessToken);
            param.Add("userid", userid);

            return Common.WebApiHelper.Post<bool>(_imApiServer + "/session/getUserOnlineState", param.AsHttpParams());

        }

        //返回visible表示UI状态是可见
        //返回hidden表示UI状态是不可见的(常见的情况是：切换到后台、屏幕关闭了等)
        public string GetClientUiState(string userid)
        {

            var param = new Dictionary<string, string>();
            param.Add("userid", userid);

            return Common.WebApiHelper.Post<string>(_imApiServer + "/client/GetClientUiState", param.AsHttpParams());

        }
        /// <summary>
        /// 查询用户的在线状态和UI状态
        /// </summary>
        public UserState GetUserState(string userid)
        {

            var param = new Dictionary<string, string>();
            param.Add("accesstoken", _accessToken);
            param.Add("userid", userid);

            return Common.WebApiHelper.Post<UserState>(_imApiServer + "/session/getUserState", param.AsHttpParams());

        }

    }
    public class UserState
    {
        public bool online { get; set; }
        public string uistate { get; set; }
    }
}
