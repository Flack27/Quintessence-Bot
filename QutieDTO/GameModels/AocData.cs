using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public class AocData
    {
        public long UserId { get; set; } 
        public long GameId { get; set; }
        public string? IGN { get; set; } 
        public int? Level { get; set; }
        public string? Class { get; set; }
        public string? Role { get; set; }
        public string? Playstyle { get; set; }

        public string? PrimaryProfession { get; set; } 
        public string? PrimaryTier { get; set; }
        public string? SecondaryProfession { get; set; }
        public string? SecondaryTier { get; set; }
        public string? TertiaryProfession { get; set; }
        public string? TertiaryTier { get; set; }
    }
}