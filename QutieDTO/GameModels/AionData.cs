using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public class AionData
    {
        public long UserId { get; set; }
        public long GameId { get; set; }
        public string? IGN { get; set; }
        public int? Gearscore { get; set; }
        public string? Class { get; set; }
        public string? Role { get; set; }
    }
}
