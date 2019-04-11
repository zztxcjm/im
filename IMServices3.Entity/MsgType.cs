using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    /// <summary>
    /// 消息类型
    /// </summary>
    public enum MsgType
    {
        //小于60的是聊天消息
        chat_text = 1,
        chat_pictures = 2,
        chat_pos = 3,
        chat_file = 4,
        chat_welcome = 5,

        //>=60是系统消息
        system_app = 60,
        system_push = 70,
        system_cmd = 80

    }
}
