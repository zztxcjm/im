using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Sdk
{

    [Serializable]
    public class UserLoginSessionInfo
    {

        /// <summary>
        /// accesstoken
        /// </summary>
        public string token { get; set; }

        /// <summary>
        /// 过期时间（这是一个Unix时间戳）
        /// </summary>
        public long expiry { get; set; }

    }

}
