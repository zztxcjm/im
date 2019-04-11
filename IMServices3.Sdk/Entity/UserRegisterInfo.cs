using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IMServices3.Entity;

namespace IMServices3.Sdk
{

    public delegate UserRegisterInfo GetUserRegisterInfo(UserType userType, string userId);

    public class UserRegisterInfo
    {

        public string UserId { get; set; }

        public string UserName { get; set; }

        public int Sex { get; set; }

        public string FaceUrl { get; set; }

        public string ExtInfo { get; set; }

    }

}
