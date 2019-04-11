using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

using FaceHand.Common.Util;
using FaceHand.Common.Exceptions;
using StackExchange.Redis;
using Newtonsoft.Json;
using IMServices3.Entity;

namespace IMServices3.Util
{

    public static class ExtFunc
    {

        public static Dictionary<string, string> AsDictionary(this HashEntry[] list, List<string> onlyIncludeKeys = null)
        {
            if (list == null || list.Length == 0)
                return new Dictionary<string, string>();

            if (onlyIncludeKeys == null || onlyIncludeKeys.Count == 0)
                return list.ToDictionary(
                    k => k.Name.ToString(), 
                    v => v.Value.HasValue ? v.Value.ToString() : string.Empty);
            else
            {
                var re = new Dictionary<string, string>();
                foreach (var item in list)
                {
                    var k = item.Name.ToString();
                    if (onlyIncludeKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
                    {
                        re.Add(k, item.Value.HasValue ? item.Value.ToString() : String.Empty);
                    }
                }
                return re;
            }
        }

        public static dynamic BuildMsg(this System.Data.DataRow row)
        {
            return new
            {
                msgid = row["MsgUid"].ToString(),
                msgtype = row["MsgType"].ToString().AsInt(),
                sender_usertype = row["SenderUserType"].ToString().AsInt(),
                sender_userid = row["SenderUserId"].ToString(),
                body = JsonConvert.DeserializeObject(row["Body"].ToString()),
                sendtime = Convert.ToDateTime(row["SendTime"]).AsUnixTimestamp(),
                extinfo = row["ExtInfo"].ToString()
            };
        }

        public static string GetStringValue(this Dictionary<string, string> dict, string key)
        {
            return GetStringValueFromDict(dict, key);
        }

        public static string NullDefault(this string str, string defaultString = null)
        {
            if (str == null)
            {
                if (defaultString == null)
                    return String.Empty;
                else
                    return defaultString;
            }

            return str;
        }

        public static string GetStringValueFromDict(Dictionary<string, string> dict, string key)
        {
            if (dict == null)
                return String.Empty;

            var re = String.Empty;
            if (dict.TryGetValue(key, out re))
            {
                return re;
            }

            return String.Empty;

        }

        public static int AsInt(this RedisResult val)
        {
            if (val == null)
                return 0;
            if (val.HasValue() && !val.IsNull && !val.IsEmpty())
                return Convert.ToInt32(val.ToString());

            return 0;

        }

        public static int AsInt(this string val)
        {
            if (String.IsNullOrEmpty(val))
                throw new BusinessException("要转换的数字不能为空");

            return Convert.ToInt32(val);

        }

        public static long AsLong(this string val)
        {
            if (String.IsNullOrEmpty(val))
                throw new BusinessException("要转换的数字不能为空");

            return Convert.ToInt64(val);

        }

        public static string AsHttpParams(this Dictionary<string, string> p)
        {
            if (p == null)
                return String.Empty;

            if (p.Keys.Count == 0)
                return String.Empty;

            var buf = new System.Text.StringBuilder();
            foreach (var k in p.Keys)
            {
                if(buf.Length>0)
                    buf.Append("&");
                buf.Append(k);
                buf.Append("=");
                var v = p[k];
                if (!String.IsNullOrEmpty(v))
                {
                    buf.Append(System.Web.HttpUtility.UrlEncode(v));
                }
            }

            return buf.ToString();

        }

        public static DateTime AsDateTimeFromUnixTimestamp(this string val)
        {
            if (String.IsNullOrEmpty(val))
                throw new BusinessException("要转换的时间不能为空");

            return FaceHand.Common.Util.DateTimeExt.FromUnixTimestamp(Convert.ToInt64(val));

        }

        public static DateTime AsDateTimeFromUnixTimestamp(this int val)
        {
            if (val == 0)
                throw new BusinessException("val是无效的UnixTimestamp");

            return FaceHand.Common.Util.DateTimeExt.FromUnixTimestamp(val);

        }

        public static T AsEnum<T>(this string val)
        {

            if (String.IsNullOrEmpty(val))
                throw new BusinessException("要转换的枚举值不能为空");

            return (T)Enum.Parse(typeof(T), val);
        }

    }

}