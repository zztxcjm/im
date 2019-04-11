using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class RedisFields
    {
        //USER相关的redis字段
        public const string USER_ID = "userid";
        public const string USER_TYPE = "usertype";
        public const string USER_NAME = "username";
        public const string USER_LOGIN_PWD = "loginpwd";
        public const string USER_SEX = "sex";
        public const string USER_FACE_URL = "faceurl";
        public const string USER_EXT_INFO = "extinfo";

        //SESSION相关的redis字段
        public const string SESSION_TOKEN = "token";
        public const string SESSION_TOKEN_USERID = "userid";
        public const string SESSION_TOKEN_CREATETIME = "createtime";
        public const string SESSION_TOKEN_CLIENTFLAG = "clientflag";

        //MSG相关的redis字段
        public const string MSG_UID = "msgid";
        public const string MSG_TYPE = "msgtype";
        public const string MSG_SEND_USERTYPE = "sender_usertype";
        public const string MSG_SEND_USERID = "sender_userid";
        public const string MSG_RECEIVERS = "receivers";
        public const string MSG_SUCCESS_RECEIVERS = "receivers_success";
        public const string MSG_FAILED_RECEIVERS = "receivers_failed";
        public const string MSG_BODY = "body";
        public const string MSG_TRYCOUNT = "trycount";
        public const string MSG_TRYTIME = "trytime";
        public const string MSG_STATE = "state";
        public const string MSG_ERROR_MSG = "errormsg";
        public const string MSG_SEND_TIME = "sendtime";
    }
}
