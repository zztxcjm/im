using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class ConstDefined
    {
        public const int LASTACTIVETIME_MAX_SECONDS = 30;

        public const string CLIENT_FLAG_WECHAT = "wechat";
        public const string CLIENT_FLAG_MINIPROGRAM = "miniprogram";
        public const string CLIENT_FLAG_SERVER = "system";

        public const string PUBCMD_SEND = "send";
        public const string PUBCMD_CLOSE = "close";
        public const string PUBCMD_CLOSEALL = "closeall";
        public const string PUBCMD_AREYOUOK = "areyouok";

    }
}
