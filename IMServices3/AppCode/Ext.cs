using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using IMServices3.Entity;
using IMServices3.Util;

namespace IMServices3
{
    public static class Ext
    {
        public static dynamic BuildMsg(this Dictionary<string, string> msg_dict)
        {
            return new
            {
                msgid = msg_dict.GetStringValue(RedisFields.MSG_UID),
                msgtype = msg_dict.GetStringValue(RedisFields.MSG_TYPE).AsInt(),
                sender_usertype = msg_dict.GetStringValue(RedisFields.MSG_SEND_USERTYPE).AsInt(),
                sender_userid = msg_dict.GetStringValue(RedisFields.MSG_SEND_USERID),
                body = Newtonsoft.Json.JsonConvert.DeserializeObject(msg_dict.GetStringValue(RedisFields.MSG_BODY)),
                sendtime = msg_dict.GetStringValue(RedisFields.MSG_SEND_TIME).AsLong(),
                extinfo = msg_dict.GetStringValue(RedisFields.USER_EXT_INFO)
            };
        }

    }
}