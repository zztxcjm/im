using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class RedisChannelName
    {
        /// <summary>
        /// send接口到消息推送器
        /// </summary>
        public const string NEW_MESSAGE_CHANNEL_TOPULLER = "newmsg1_topuller";

        /// <summary>
        /// 消息推送器到客户端
        /// </summary>
        public const string NEW_MESSAGE_CHANNEL_TOCLIENT = "newmsg_toclient";

    }
}
