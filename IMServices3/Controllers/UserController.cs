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
    public class UserController : BaseController
    {

        private static Regex _userid_regex = new Regex("[^a-zA-Z0-9]", RegexOptions.IgnoreCase);
        private static Regex _loginpwd_regex = new Regex("\\s", RegexOptions.IgnoreCase);

        /// <summary>
        /// 注册im账号
        /// </summary>
        /// <param name="userType">用户类型（employee,system,app,openid）</param>
        /// <param name="userId">userid是用户在im系统中的唯一账号，由业务层构建，必须保证唯一，一旦注册不可修改（长度不超过50个字符）</param>
        /// <param name="userName">用户名字（长度不超过50个字符）</param>
        /// <param name="loginPwd">用户登录密码(长度6-50位字符)</param>
        /// <param name="sex">用户性别 0-男，1-女</param>
        /// <param name="faceUrl">头像地址(长度不超过2KB)</param>
        /// <param name="extInfo">自定义扩展信息（长度不超过2KB）</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult Register(UserType userType, string userId, string userName, string loginPwd, int sex, string faceUrl, string extInfo)
        {

            if (String.IsNullOrEmpty(userId))
                throw new BusinessException("userid不能为空", 10001);
            if (userId.Length > 50)
                throw new BusinessException("userid不能超过50个字符", 10002);
            if (_userid_regex.IsMatch(userId))
                throw new BusinessException("userid只能包含a-z,A-Z,0-9", 10005);

            if (String.IsNullOrEmpty(userName))
                throw new BusinessException("username不能为空", 10010);
            if (userName.Length > 50)
                throw new BusinessException("username不能超过50个字符", 10011);

            if (String.IsNullOrEmpty(loginPwd))
                throw new BusinessException("loginPwd不能为空", 10020);
            if (loginPwd.Length > 20 || loginPwd.Length < 6)
                throw new BusinessException("loginPwd长度应为6-20个字符", 10021);
            if (_loginpwd_regex.IsMatch(loginPwd))
                throw new BusinessException("loginPwd不能包含空白字符", 10023);

            if (!String.IsNullOrEmpty(faceUrl))
            {
                if (!faceUrl.StartsWith("http://") && !faceUrl.StartsWith("https://"))
                    throw new BusinessException("faceUrl必须以http或https开头", 10030);
                if (System.Text.Encoding.UTF8.GetBytes(faceUrl).Length > 2048)
                    throw new BusinessException("faceUrl不能超过2KB", 10031);
            }
            if (!String.IsNullOrEmpty(extInfo) && System.Text.Encoding.UTF8.GetBytes(extInfo).Length > 2048)
                throw new BusinessException("extInfo不能超过2KB", 10031);

            if (sex != 1)
                sex = 0;//默认是男，修正乱传的数据

            var userinfoKey = $"im_userinfo_{userId}";
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_USER_KEY);
            if (redis.Exec<bool>(db => db.KeyExists(userinfoKey)))
                throw new BusinessException("userid已存在", 10003);

            redis.Exec(db =>
            {

                var batch = db.CreateBatch();

                batch.HashSetAsync(userinfoKey, new HashEntry[] {
                    new HashEntry(RedisFields.USER_ID, userId),
                    new HashEntry(RedisFields.USER_NAME, userName),
                    new HashEntry(RedisFields.USER_TYPE, (int)userType),
                    new HashEntry(RedisFields.USER_LOGIN_PWD, FaceHand.Common.Util.StringExt.GetSHA1HashCode(loginPwd)),
                    new HashEntry(RedisFields.USER_SEX, sex),
                    new HashEntry(RedisFields.USER_FACE_URL, faceUrl.NullDefault()),
                    new HashEntry(RedisFields.USER_EXT_INFO, extInfo.NullDefault())
                });
                batch.SortedSetAddAsync("im_userlist", userId, DateTime.Now.AsUnixTimestamp());

                batch.Execute();

            });

            return JsonContent<bool>(true);

        }

        /// <summary>
        /// 获取用户信息
        /// </summary>
        /// <param name="userid">im用户id</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult Get(string userid)
        {
            var userinfo = IMRedisDAL.GetUserInfo(userid);
            return JsonContent(new {
                userid = userinfo.GetStringValue(RedisFields.USER_ID),
                username = userinfo.GetStringValue(RedisFields.USER_NAME),
                usertype = userinfo.GetStringValue(RedisFields.USER_TYPE).AsInt(),
                //loginpwd = userinfo.GetStringValue(RedisFields.USER_LOGIN_PWD),
                sex = userinfo.GetStringValue(RedisFields.USER_SEX).AsInt(),
                faceurl = userinfo.GetStringValue(RedisFields.USER_FACE_URL),
                extinfo = userinfo.GetStringValue(RedisFields.USER_EXT_INFO)
            });
        }

        /// <summary>
        /// 修改im账号信息
        /// </summary>
        /// <param name="userid">im用户id</param>
        /// <returns></returns>
        [HttpPost]
        [ActionExceptionHandler]
        public ActionResult Edit(string userid)
        {

            //username, loginpwd, sex, faceUrl, extinfo
            //从post参数中取值，存在就修改，不存在就不修改

            if (String.IsNullOrEmpty(userid))
                throw new BusinessException("userid不能为空", 10001);

            var userinfoKey = $"im_userinfo_{userid}";
            var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_USER_KEY);
            var isexist = redis.Exec<bool>(db => db.KeyExists(userinfoKey));
            if (!isexist)
            {
                throw new BusinessException("userid不存在", 10004);
            }

            var dict = new Dictionary<string, string>();
            dict.Add(RedisFields.USER_NAME, Request.Form[RedisFields.USER_NAME]);
            dict.Add(RedisFields.USER_LOGIN_PWD, Request.Form[RedisFields.USER_LOGIN_PWD]);
            dict.Add(RedisFields.USER_FACE_URL, Request.Form[RedisFields.USER_FACE_URL]);
            dict.Add(RedisFields.USER_EXT_INFO, Request.Form[RedisFields.USER_EXT_INFO]);
            dict.Add(RedisFields.USER_SEX, Request.Form[RedisFields.USER_SEX]);

            var needUpdateField = new List<HashEntry>();
            foreach (string k in dict.Keys)
            {
                var v = dict[k];
                if (v != null)
                {
                    if (k == RedisFields.USER_LOGIN_PWD)
                        needUpdateField.Add(new HashEntry(k, FaceHand.Common.Util.StringExt.GetSHA1HashCode(v)));
                    else
                        needUpdateField.Add(new HashEntry(k, v));
                }
            }

            if (needUpdateField.Count > 0)
            {
                redis.Exec(db => db.HashSet(userinfoKey, needUpdateField.ToArray()));
            }

            return JsonContent<bool>(true);

        }

        /// <summary>
        /// 删除im账号
        /// </summary>
        /// <param name="useridlist">im用户id,多个userid之间用“,”分割</param>
        /// <returns></returns>
        [ActionExceptionHandler]
        public ActionResult Remove(string useridlist)
        {

            if (!String.IsNullOrEmpty(useridlist))
            {
                IEnumerable<string> lst = null;
                if (useridlist.IndexOf(",") == -1)
                {
                    lst = new List<string>() { useridlist.Trim() };
                }
                else
                {
                    lst = useridlist.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                }

                //要删除的key
                var userlist_memberlist = new List<RedisValue>();
                var userinfo_keylist = new List<RedisKey>();
                var session_keylist = new List<RedisKey>();
                var msgcache_keylist = new List<RedisKey>();
                foreach (var userid in lst)
                {
                    //基础数据
                    userlist_memberlist.Add(userid);
                    userinfo_keylist.Add($"im_userinfo_{userid}");

                    //会话数据
                    session_keylist.Add($"imuser_user_token_{userid}");
                    session_keylist.Add($"imuser_user_clientstate_{userid}");
                    session_keylist.Add($"imuser_lastactivetime_{userid}");

                    //消息缓存
                    msgcache_keylist.Add($"notreadcount_{userid}");
                    msgcache_keylist.Add($"notreadlist_{userid}");

                }


                //删除基础数据
                var redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_USER_KEY);
                redis.Exec(db => {
                    var batch = db.CreateBatch();
                    batch.KeyDeleteAsync(userinfo_keylist.ToArray(), CommandFlags.FireAndForget);
                    batch.SortedSetRemoveAsync("im_userlist", userlist_memberlist.ToArray(), CommandFlags.FireAndForget);
                    batch.Execute();
                });

                //删除会话数据
                redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_SESSION_KEY);
                redis.Exec(db => db.KeyDelete(session_keylist.ToArray(), CommandFlags.FireAndForget));

                //删除消息缓存
                redis = new FaceHand.Common.ResdisExecutor(RedisConfig.REDIS_MSGPOST_KEY);
                redis.Exec(db => db.KeyDelete(msgcache_keylist.ToArray(), CommandFlags.FireAndForget));

            }

            return JsonContent<bool>(true);

        }

    }
}