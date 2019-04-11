using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class MessageSendStateQueue
    {
        //等待首次发送的队列
        public const string SENDING = "MSSQ_Sending";

        //首次发送失败，等待后续尝试的队列
        public const string TRYING = "MSSQ_Trying";

        //public const string SUCCESS = "MSSQ_Success";
        //public const string FAILED = "MSSQ_Failed";

    }
}
