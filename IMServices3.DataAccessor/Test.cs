using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.DataAccessor
{
    class Test
    {
        public static void Main(string[] args)
        {

            var msg_uid = Guid.NewGuid().ToString();
            var receiver_valid = new List<string>();
            for (int i = 0; i < 10001; i++)
            {
                receiver_valid.Add(new Random().Next(50000, 99999).ToString());
            }



            //写接收人
            var sql2 = @"insert into im_message_receivers (MsgUid,Receiver_UserId) values {0};";
            //insert into im_message_receivers (MsgUid,Receiver_UserId) 
            //    value ('MsgUid','Receiver_UserId'),('MsgUid','Receiver_UserId'),('MsgUid','Receiver_UserId');

            var pageSize = 1000;
            var totalCount = receiver_valid.Count;

            if (totalCount <= pageSize)
            {
                var sql3 = String.Format(sql2, $"('{msg_uid}','{String.Join($"'),('{msg_uid}','", receiver_valid)}')");
                Console.WriteLine(sql3);
            }
            else
            {
                var pageCount = totalCount / pageSize + (totalCount % pageSize != 0 ? 1 : 0);
                var sqlBuf = new StringBuilder();
                for (int i = 0; i < pageCount; i++)
                {
                    int startIndex = i * pageSize;
                    sqlBuf.AppendFormat(sql2 + "\r\n", $"('{msg_uid}','{String.Join($"'),('{msg_uid}','", receiver_valid.GetRange(startIndex, Math.Min(pageSize, totalCount - startIndex)))}')");
                }
                Console.WriteLine(sqlBuf.ToString());

            }

            Console.ReadLine();

        }
    }
}
