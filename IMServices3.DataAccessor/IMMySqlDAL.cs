using System;
using System.Text;
using System.Data;
using System.Data.Common;
using System.Collections.Generic;
using System.Linq;

using FaceHand.Common.Exceptions;
using FaceHand.Common.Util;
using IMServices3.Entity;
using IMServices3.Util;

using StackExchange.Redis;

namespace IMServices3.DataAccessor
{
    public class IMMySqlDAL : FaceHand.Common.DataAccessBase
    {

        public static IMMySqlDAL _instance = null;
        public static object _lock = new object();
        public static IMMySqlDAL Instance
        {
            get
            {
                if(_instance==null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new IMMySqlDAL();
                        }
                    }
                }

                return _instance;
            }
        }

        private IMMySqlDAL() { }

        public void InsertMsg(Dictionary<string, string> msg, List<string> receiver_valid)
        {

            //im_message            Id,MsgUid,MsgType,SenderUserType,SenderUserId,Body,SendTime
            //im_message_receivers  Id,MsgUid,ReceiverUserId

            var database = this.DbInstance;

            //写消息
            var sql1 = @"insert into im_message (MsgUid,MsgType,SenderUserType,SenderUserId,SendTime,Body,ExtInfo) 
                            values(@MsgUid,@MsgType,@SenderUserType,@SenderUserId,@SendTime,@Body,@ExtInfo)";

            using (DbCommand cmd = database.GetSqlStringCommand(sql1))
            {
                database.AddInParameter(cmd, "MsgUid", DbType.String, msg.GetStringValue(RedisFields.MSG_UID));
                database.AddInParameter(cmd, "MsgType", DbType.Byte, msg.GetStringValue(RedisFields.MSG_TYPE).AsEnum<MsgType>());
                database.AddInParameter(cmd, "SenderUserType", DbType.Byte, msg.GetStringValue(RedisFields.MSG_SEND_USERTYPE).AsEnum<UserType>());
                database.AddInParameter(cmd, "SenderUserId", DbType.String, msg.GetStringValue(RedisFields.MSG_SEND_USERID));
                database.AddInParameter(cmd, "SendTime", DbType.DateTime, msg.GetStringValue(RedisFields.MSG_SEND_TIME).AsDateTimeFromUnixTimestamp());
                database.AddInParameter(cmd, "Body", DbType.String, msg.GetStringValue(RedisFields.MSG_BODY));
                database.AddInParameter(cmd, "ExtInfo", DbType.String, msg.GetStringValue(RedisFields.USER_EXT_INFO));
                database.ExecuteNonQuery(cmd);
            }


            //写接收人
            var sql2 = @"insert into im_message_receivers (MsgUid,ReceiverUserId) values {0};";
            //insert into im_message_receivers (MsgUid,Receiver_UserId) 
            //    value ('MsgUid','ReceiverUserId'),('MsgUid','ReceiverUserId'),('MsgUid','ReceiverUserId');

            var msg_uid = msg.GetStringValue(RedisFields.MSG_UID);

            var pageSize = 500;
            var totalCount = receiver_valid.Count;

            if (totalCount <= pageSize)
            {
                var sql3 = String.Format(sql2, $"('{msg_uid}','{String.Join($"'),('{msg_uid}','", receiver_valid)}')");
                ExecSql(database.GetSqlStringCommand(sql3));
            }
            else
            {
                var pageCount = totalCount / pageSize + (totalCount % pageSize != 0 ? 1 : 0);
                for (int i = 0; i < pageCount; i++)
                {
                    int startIndex = i * pageSize;
                    var sql3 = String.Format(sql2, $"('{msg_uid}','{String.Join($"'),('{msg_uid}','", receiver_valid.GetRange(startIndex, Math.Min(pageSize, totalCount - startIndex)))}')");
                    ExecSql(database.GetSqlStringCommand(sql3));
                }

            }

        }

        public DataTable SelectChatMessageList(string userid, string chatuserid, int pageIndex, int pageSize)
        {
            var sql = @"select 
                            DISTINCT a.MsgUid,a.MsgType,a.SenderUserType,a.SenderUserId,a.SendTime,a.Body,a.ExtInfo
                        from 
	                        im_message as a
	                        left join im_message_receivers as b on (a.MsgUid=b.MsgUid)
                        where 
                            a.MsgType<60
                            and (a.SenderUserId=@SenderUserID or a.SenderUserId=@RecevierUserID)
                            and (b.ReceiverUserId=@SenderUserID or b.ReceiverUserId=@RecevierUserID)
                        order by a.SendTime desc
                        limit " + ((pageIndex - 1) * pageSize) + @"," + pageSize;

            var db = this.DbInstance;
            using (DbCommand cmd = db.GetSqlStringCommand(sql))
            {
                db.AddInParameter(cmd, "SenderUserID", DbType.String, userid);
                db.AddInParameter(cmd, "RecevierUserID", DbType.String, chatuserid);

                return GetDataTable(cmd);

            }

        }
    }
}
