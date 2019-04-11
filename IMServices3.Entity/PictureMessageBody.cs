using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IMServices3.Entity
{
    public class PictureMessageBody : ChatMessageBody
    {
        public class Picture
        {
            public string orgUrl { get; set; }
            public string thubUrl { get; set; }
        }

        public IEnumerable<Picture> pictures { get; set; }
    }

}
