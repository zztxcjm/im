using System;
using System.Collections.Generic;
using System.Linq;

using FaceHand.Common.Exceptions;
using FaceHand.Common.Util;
using StackExchange.Redis;

using IMServices3.Entity;
using IMServices3.Util;

namespace IMServices3.DataAccessor
{
    public class IMRedisDAL
    {

        public static Dictionary<string, string> GetUserInfo(string userid)
        {

            if (String.IsNullOrEmpty(userid))
                throw new BusinessException("userid不能为空", 10001);

            var userinfoKey = $"im_userinfo_{userid}";
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_USER_KEY);
            var returnval = redis.Exec<Dictionary<string, string>>(db => db.HashGetAll(userinfoKey).AsDictionary());

            if (returnval == null || returnval.Count == 0)
            {
                throw new BusinessException("userid不存在", 10004);
            }

            return returnval;

        }

        public static string GetUserIdByAccessToken(string accesstoken)
        {
            if (String.IsNullOrEmpty(accesstoken))
                throw new BusinessException("accesstoken不能为空", 20001);

            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
            return redis.Exec<string>(db => {

                //能获取到这个key就说明token还没过期
                var userid = db.HashGet($"imuser_token_user_{accesstoken}", RedisFields.SESSION_TOKEN_USERID);
                if (userid.IsNull)
                {
                    db.SetRemove($"imuser_user_token_{userid}", accesstoken, CommandFlags.FireAndForget);
                    throw new BusinessException("accesstoken已过期", 20002);
                }

                return userid;

            });

        }

        public static Dictionary<string, string> GetMsg(string msg_uid)
        {
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGCACHE_KEY);
            return redis.Exec<Dictionary<string, string>>(db => db.HashGetAll($"msg_{msg_uid}").AsDictionary());
        }

    }
}