using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    /// <summary>
    /// 聊天消息类型
    /// </summary>
    public enum ChatMsgType
    {
        //这个定义是为了使用方便，兼容MsgType中的定义

        text = 1,
        pictures = 2,
        pos = 3,
        file = 4,
        welcome = 5

    }
}
