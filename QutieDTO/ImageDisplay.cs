using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO
{
    public class ImageDisplay
    {
        public string Name { get; set; }
        public string FallBackName { get; set; }
        public string Avatar { get; set; }
        public int VoiceLevel { get; set; }
        public int VoiceXP { get; set; }
        public int VoiceReqXP { get; set; }
        public int VoiceRank { get; set; }
        public int MessageLevel { get; set; }
        public int MessageXP { get; set; }
        public int MessageReqXP { get; set; }
        public int MessageRank { get; set; }
        public double Karma { get; set; }
    }
}
