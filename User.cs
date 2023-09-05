using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Extensions.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace IgisBot
{
    enum CurrentExpectedRequest
    {
        Location,
        Transport,
        StopName,
        None
    }
    internal class User
    {
        public long Id { get; set; }
        public long ChatId { get; set; }
        public string Type { get; set; }
        public CurrentExpectedRequest Request = CurrentExpectedRequest.None;
        public string Data { get; set; }
        public int Route { get; set; }
        public int Stop { get; set; }
        public Location Location { get; set; }
        public string FinishStop { get; set; }
        public List<int> FavStops { get; set; }
    }
}
