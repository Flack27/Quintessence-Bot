using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QutieBot.Bot.Commands
{
    [AttributeUsage(AttributeTargets.Method)]
    public class GameCommandAttribute : Attribute
    {
        public string CommandName { get; }
        public string Description { get; }
        public long GameId { get; }

        public GameCommandAttribute(string commandName, string description, long gameId)
        {
            CommandName = commandName;
            Description = description;
            GameId = gameId;
        }
    }
}
