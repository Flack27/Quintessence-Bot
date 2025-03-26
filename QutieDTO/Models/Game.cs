using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieDTO.Models
{
    public partial class Game
    {
        public long GameId { get; set; }

        public long RoleId { get; set; }

        public string GameName { get; set; } = null!;
        public string SheetId { get; set; } = null!;
        public long ChannelId { get; set; }

        public virtual ICollection<Channel> Channels { get; set; } = new List<Channel>();

        public virtual Channel? Channel { get; set; }
        public virtual Role? Role { get; set; }
    }

}
