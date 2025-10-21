using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public class WwmData
    {
        public long UserId { get; set; }
        public long GameId { get; set; }
        public string? IGN { get; set; }
        public int? Level { get; set; }
        public string? PrimaryWeapon { get; set; }
        public string? SecondaryWeapon { get; set; }
        public string? Role { get; set; }
        public string? Playstyle { get; set; }
    }
}
