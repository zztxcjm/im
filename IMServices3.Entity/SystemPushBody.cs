using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class SystemPushBody : MessageBody
    {
        /// <summary>
        /// 自己定义业务类型
        /// </summary>
        public string type { get; set; }
        /// <summary>
        /// 自定义业务数据
        /// </summary>
        public string data { get; set; }
    }
}
