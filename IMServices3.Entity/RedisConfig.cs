using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class RedisConfig
    {
        /// <summary>
        /// 存储未读消息列表和未读消息数量
        /// </summary>
        public const string REDIS_MSGPOST_KEY = "REDIS_MSGPOST_KEY";

        /// <summary>
        /// 消息发送中转消息队列（待发送队列，和消息推送器之间的订阅器）
        /// </summary>
        public const string REDIS_MSGMQ_KEY = "REDIS_MSGMQ_KEY";

        /// <summary>
        /// 用于保存一些状态数据
        /// </summary>
        public const string REDIS_STATE_KEY = "REDIS_STATE_KEY";

        /// <summary>
        /// 存储消息的完整结构
        /// </summary>
        public const string REDIS_MSGCACHE_KEY = "REDIS_MSGCACHE_KEY";

        /// <summary>
        /// 存储账号注册信息
        /// </summary>
        public const string REDIS_USER_KEY = "REDIS_USER_KEY";

        /// <summary>
        /// 存储登陆会话和token
        /// </summary>
        public const string REDIS_SESSION_KEY = "REDIS_SESSION_KEY";

    }
}
